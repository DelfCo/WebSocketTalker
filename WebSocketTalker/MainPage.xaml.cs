using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using Windows.UI.Core;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace WebSocketTalker
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private MessageWebSocket mws;
        private DataWriter messageWriter;

        public enum NotifyType
        {
            StatusMessage,
            ErrorMessage
        };

        /// <summary>
        /// Used to display messages to the user
        /// </summary>
        /// <param name="strMessage"></param>
        /// <param name="type"></param>
        public void NotifyUser(string strMessage, NotifyType type)
        {
            switch (type)
            {
                case NotifyType.StatusMessage:
                    StatusBorder.Background = new SolidColorBrush(Windows.UI.Colors.Green);
                    break;
                case NotifyType.ErrorMessage:
                    StatusBorder.Background = new SolidColorBrush(Windows.UI.Colors.Red);
                    break;
            }
            StatusBlock.Text = strMessage;

            // Collapse the StatusBlock if it has no text to conserve real estate.
            StatusBorder.Visibility = (StatusBlock.Text != String.Empty) ? Visibility.Visible : Visibility.Collapsed;
            if (StatusBlock.Text != String.Empty)
            {
                StatusBorder.Visibility = Visibility.Visible;
                StatusPanel.Visibility = Visibility.Visible;
            }
            else
            {
                StatusBorder.Visibility = Visibility.Collapsed;
                StatusPanel.Visibility = Visibility.Collapsed;
            }
        }

        public bool TryGetUri(string uriString, out Uri uri)
        {
            uri = null;

            Uri webSocketUri;
            if (!Uri.TryCreate(uriString.Trim(), UriKind.Absolute, out webSocketUri))
            {
                NotifyUser("Error: Invalid URI", NotifyType.ErrorMessage);
                return false;
            }

            // Fragments are not allowed in WebSocket URIs.
            if (!String.IsNullOrEmpty(webSocketUri.Fragment))
            {
                NotifyUser("Error: URI fragments not supported in WebSocket URIs.", NotifyType.ErrorMessage);
                return false;
            }

            // Uri.SchemeName returns the canonicalized scheme name so we can use case-sensitive, ordinal string
            // comparison.
            if ((webSocketUri.Scheme != "ws") && (webSocketUri.Scheme != "wss"))
            {
                NotifyUser("Error: WebSockets only support ws:// and wss:// schemes.", NotifyType.ErrorMessage);
                return false;
            }

            uri = webSocketUri;

            return true;
        }

        private void MarshalText(TextBox output, string value, bool append)
        {
            var ignore = output.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                if (append)
                {
                    output.Text += value;
                }
                else
                {
                    output.Text = value;
                }
            });
        }
        private void MarshalText(TextBox output, string value)
        {
            MarshalText(output, value, true);
        }


        public MainPage()
        {
            this.InitializeComponent();
        }

        private async void connectButton_Click(object sender, RoutedEventArgs e)
        {
            Uri uri;

            if (mws == null)
            {
                NotifyUser("Starting to connect", NotifyType.StatusMessage);

                // validate the URI
                if (TryGetUri(this.urlBox.Text, out uri))
                {
                    try
                    {
                        NotifyUser("URI is good. Connecting.", NotifyType.StatusMessage);

                        mws = new MessageWebSocket();
                        // sending UTF8 as Binary will make our target web service wait 30 seconds before echoing.
                        mws.Control.MessageType = SocketMessageType.Binary;

                        // add a mws.MessageRecieved handler here, to receive data from the connected remote endpoint
                        mws.MessageReceived += Mws_MessageReceived;
                        // add a mws.Closed hander here, to properly maintain state when the other end closes the connection
                        // Dispatch close event on UI thread. This allows us to avoid synchronizing access to messageWebSocket.
                        mws.Closed += async (senderSocket, args) =>
                        {
                            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => Mws_Closed(senderSocket, args));
                        };

                        // connect on the websocket
                        await mws.ConnectAsync(uri);
                        NotifyUser("Connected.", NotifyType.StatusMessage);
                        connectButton.Content = "Disconnect";

                        messageWriter = new DataWriter(mws.OutputStream);

                    }
                    catch (Exception ex)
                    {
                        string msg = ex.Message;
                        NotifyUser("Error connecting: " + msg, NotifyType.ErrorMessage);
                    }
                }
                else
                {
                    NotifyUser("Invalid URI", NotifyType.ErrorMessage);
                }
            }
            else
            {
                NotifyUser("Disconnecting.", NotifyType.StatusMessage);
                // disconnecting
                try
                {
                    mws.Close(1000, "opening new one");
                    mws = null;
                    NotifyUser("Disconnected", NotifyType.StatusMessage);
                    connectButton.Content = "Connect";
                }
                catch (Exception ex)
                {
                    string msg = ex.Message;
                    NotifyUser("Error disconnecting: " + msg, NotifyType.ErrorMessage);
                }
            }
        }

        private void Mws_MessageReceived(MessageWebSocket sender, MessageWebSocketMessageReceivedEventArgs args)
        {
            // TODO
            // 1. get the message out of the args.
            // 2. add it to the outputBox content. 
            // Note that you're not necessarily on the UI thread when called asynchronously, 
            // and marshall over to the UI thread rather than just stuffing the UI element.
            using (DataReader reader = args.GetDataReader())
            {
                reader.UnicodeEncoding = Windows.Storage.Streams.UnicodeEncoding.Utf8;

                string read = reader.ReadString(reader.UnconsumedBufferLength);
                MarshalText(outputBox, read + "\r\n");
            }

        }

        private void Mws_Closed(IWebSocket sender, WebSocketClosedEventArgs args)
        {
            if (mws != null)
            {
                mws.Dispose();
                mws = null;
            }
            //NotifyUser("Disconnected", NotifyType.StatusMessage);
            //connectButton.Content = "Connect";
        }

        private async void sendButton_Click(object sender, RoutedEventArgs e)
        {
            // Buffer data we want to send.
            messageWriter.WriteString(inputBox.Text);

            MarshalText(outputBox, "wait for it...\r\n");

            // Send the data as one complete message. 
            // The messageWriter DataWriter is already associated with the MessageWebSocket's OutputStream,
            // so storing the new contents of the DataWriter sends it out on the WebSocket.
            await messageWriter.StoreAsync();

        }

    }
}

