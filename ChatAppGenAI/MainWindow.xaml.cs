using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace ChatAppGenAI
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        private VM VM;

        public MainWindow()
        {
            this.InitializeComponent();
            VM = new VM(DispatcherQueue);
        }

        private async void TextBox_KeyUp(object sender, KeyRoutedEventArgs e)
        {
            var textBox = sender as TextBox;
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                if (textBox.Text.Length > 0)
                {
                    VM.AddMessage(textBox.Text);
                    textBox.Text = string.Empty;
                }
            }
        }
        public static SolidColorBrush PhiMessageTypeToColor(PhiMessageType type)
        {
            return (type == PhiMessageType.User) ? new SolidColorBrush(Colors.White) : new SolidColorBrush(Color.FromArgb(255, 68, 228, 255));
        }

        public static SolidColorBrush PhiMessageTypeToForeground(PhiMessageType type)
        {
            return (type == PhiMessageType.User) ? new SolidColorBrush(Colors.Black) : new SolidColorBrush(Color.FromArgb(255, 80, 80, 80));
        }

        public static Visibility BoolToVisibleInversed(bool value)
        {
            return value ? Visibility.Collapsed : Visibility.Visible;
        }
    }
    public partial class VM: ObservableObject
    {
        public ObservableCollection<Message> Messages = new();

        [ObservableProperty]
        public bool acceptsMessages;

        private ModelRunner phi3 = new();
        private DispatcherQueue dispatcherQueue;

        public VM(DispatcherQueue dispatcherQueue)
        {
            phi3.ModelLoaded += Phi3_ModelLoaded;
            phi3.InitializeAsync();
            this.dispatcherQueue = dispatcherQueue;
        }

        private void Phi3_ModelLoaded(object sender, EventArgs e)
        {
            dispatcherQueue.TryEnqueue(() =>
            {
                AcceptsMessages = true;
            });
        }

        public void AddMessage(string text)
        {
            AcceptsMessages = false;
            Messages.Add(new Message(text, DateTime.Now, PhiMessageType.User));

            Task.Run(async () =>
            {
                var systemPrompt = "You are a helpful assistant.";
                var history = Messages.Select(m => new PhiMessage(m.Text, m.Type)).ToList();

                var responseMessage = new Message("...", DateTime.Now, PhiMessageType.Assistant);

                dispatcherQueue.TryEnqueue(async () =>
                {
                    await Task.Delay(1000);
                    Messages.Add(responseMessage);
                });

                bool firstPart = true;

                await foreach (var messagePart in phi3.InferStreaming(systemPrompt, history, text))
                {
                    var part = messagePart;
                    dispatcherQueue.TryEnqueue(() =>
                    {
                        if (firstPart)
                        {
                            responseMessage.Text = string.Empty;
                            firstPart = false;
                            part = messagePart.TrimStart();
                        }

                        responseMessage.Text += part;
                    });
                }

                dispatcherQueue.TryEnqueue(() =>
                {
                    AcceptsMessages = true;
                });
            });
        }
    }

    public partial class Message : ObservableObject
    {
        [ObservableProperty]
        public string text;
        public DateTime MsgDateTime { get; private set; }

        public PhiMessageType Type { get; set; }
        public HorizontalAlignment MsgAlignment => Type == PhiMessageType.User ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        public Message(string text, DateTime dateTime, PhiMessageType type)
        {
            Text = text;
            MsgDateTime = dateTime;
            Type = type;
        }

        public override string ToString()
        {
            return MsgDateTime.ToString() + " " + Text;
        }
    }
}
