using System;
using Microsoft.ML.OnnxRuntimeGenAI;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Windows.Services.Maps;
using System.Runtime.CompilerServices;
using System.Threading;

namespace ChatAppGenAI
{
    public class ModelRunner : IDisposable
    {
        // huggingface-cli download microsoft/Phi-3-mini-4k-instruct-onnx --include directml/* --local-dir .
        private readonly string ModelDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"Models\directml\directml-int4-awq-block-128");
        //private readonly string ModelDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"Models\directml\mistralai_Mistral-7B-Instruct-v0.2");
        //private readonly string ModelDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"Models\directml\Llama-3-8B-Instruct-Onnx"); 

        private Model? model = null;
        private Tokenizer? tokenizer = null;
        public event EventHandler? ModelLoaded = null;

        [MemberNotNullWhen(true, nameof(model), nameof(tokenizer))]
        public bool IsReady => model != null && tokenizer != null;

        public void Dispose()
        {
            model?.Dispose();
            tokenizer?.Dispose();
        }

        public IAsyncEnumerable<string> InferStreaming(string systemPrompt, string userPrompt, [EnumeratorCancellation] CancellationToken ct = default)
        {
            var prompt = $@"<|system|>{systemPrompt}<|end|><|user|>{userPrompt}<|end|><|assistant|>";
            return InferStreaming(prompt, ct);
        }
        
        public IAsyncEnumerable<string> InferStreaming(string systemPrompt, List<PhiMessage> history, string userPrompt, [EnumeratorCancellation] CancellationToken ct = default)
        {
            var prompt = $@"<|system|>{systemPrompt}<|end|>";
            foreach (var message in history)
            {
                prompt += $"<|{message.Type.ToString().ToLower()}|>{message.Text}<|end|>";
            }
            prompt += "<|assistant|>";

            return InferStreaming(prompt, ct);

        }
        public async IAsyncEnumerable<string> InferStreaming(string prompt, [EnumeratorCancellation] CancellationToken ct = default)
        {
            if (!IsReady)
            {
                throw new InvalidOperationException("Model is not ready");
            }

            var generatorParams = new GeneratorParams(model);

            var sequences = tokenizer.Encode(prompt);

            generatorParams.SetSearchOption("max_length", 1024);
            generatorParams.SetInputSequences(sequences);
            generatorParams.TryGraphCaptureWithMaxBatchSize(1);

            using var tokenizerStream = tokenizer.CreateStream();
            using var generator = new Generator(model, generatorParams);
            StringBuilder stringBuilder = new();
            while (!generator.IsDone())
            {
                string part;
                try
                {
                    if (ct.IsCancellationRequested)
                    {
                        break;
                    }

                    generator.ComputeLogits();
                    generator.GenerateNextToken();
                    part = tokenizerStream.Decode(generator.GetSequence(0)[^1]);
                    stringBuilder.Append(part);
                    if (stringBuilder.ToString().Contains("<|end|>")
                        || stringBuilder.ToString().Contains("<|user|>")
                        || stringBuilder.ToString().Contains("<|system|>"))
                    {
                        break;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                    break;
                }

                yield return part;
            }
        }

        public Task InitializeAsync()
        {
            return Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();
                model = new Model(ModelDir);
                tokenizer = new Tokenizer(model);
                sw.Stop();
                Debug.WriteLine($"Model loading took {sw.ElapsedMilliseconds} ms");
                ModelLoaded?.Invoke(this, EventArgs.Empty);
            });
        }
    }

    public class PhiMessage
    {
        public string Text { get; set; }
        public PhiMessageType Type { get; set; }

        public PhiMessage(string text, PhiMessageType type)
        {
            Text = text;
            Type = type;
        }
    }

    public enum  PhiMessageType
    {
        User,
        Assistant
    }
}