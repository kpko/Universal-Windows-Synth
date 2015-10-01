using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Devices.Enumeration;
using Windows.Devices.Midi;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Media;
using Windows.Media.Audio;
using Windows.Media.Render;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace App1
{
    // We are initializing a COM interface for use within the namespace
    // This interface allows access to memory at the byte level which we need to populate audio data that is generated
    [ComImport]
    [Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]

    unsafe interface IMemoryBufferByteAccess
    {
        void GetBuffer(out byte* buffer, out uint capacity);
    }

    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        ApplicationView view;
        AudioGraph graph;
        CreateAudioDeviceOutputNodeResult output;
        AudioFrameInputNode input;
        MidiInPort port;

        float[] notes = new float[127];

        public MainPage()
        {
            this.InitializeComponent();
            view = ApplicationView.GetForCurrentView();
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            // midi

            var s = MidiInPort.GetDeviceSelector();
            var information = await DeviceInformation.FindAllAsync(s);

            var list = information.ToList();
            port = await MidiInPort.FromIdAsync(list.ElementAt(2).Id);
            port.MessageReceived += Port_MessageReceived;

            // audio
            var settings = new AudioGraphSettings(AudioRenderCategory.GameEffects);
            settings.QuantumSizeSelectionMode = QuantumSizeSelectionMode.LowestLatency;
            var creation = await AudioGraph.CreateAsync(settings);

            graph = creation.Graph;
            output = await graph.CreateDeviceOutputNodeAsync();

            var encoding = graph.EncodingProperties;
            encoding.ChannelCount = 1;
            input = graph.CreateFrameInputNode(encoding);
            input.AddOutgoingConnection(output.DeviceOutputNode);
            input.Stop();

            input.QuantumStarted += Input_QuantumStarted;

            graph.Start();

            // midi notes (pitch to note)

            float a = 440; // a is 440 hz...
            for (int x = 0; x < 127; ++x)
            {
                notes[x] = (a / 32f) * (float)Math.Pow(2f, ((x - 9f) / 12f));
            }
        }

        private void Input_QuantumStarted(AudioFrameInputNode sender, FrameInputNodeQuantumStartedEventArgs args)
        {
            uint samplesNeeded = (uint)args.RequiredSamples;

            if (samplesNeeded != 0)
            {
                AudioFrame frame = GenerateAudio(samplesNeeded);
                input.AddFrame(frame);
            }
        }

        public double theta = 0;
        float freq = 1000; // choosing to generate frequency of 1kHz

        unsafe private AudioFrame GenerateAudio(uint samplesNeeded)
        {
            // Buffer size is (number of samples) * (size of each sample)
            // We choose to generate single channel (mono) audio. For multi-channel, multiply by number of channels
            uint bufferSize = samplesNeeded * sizeof(float);
            AudioFrame frame = new Windows.Media.AudioFrame(bufferSize);

            using (AudioBuffer buffer = frame.LockBuffer(AudioBufferAccessMode.Write))
            using (IMemoryBufferReference reference = buffer.CreateReference())
            {
                byte* dataInBytes;
                uint capacityInBytes;
                float* dataInFloat;

                // Get the buffer from the AudioFrame
                ((IMemoryBufferByteAccess)reference).GetBuffer(out dataInBytes, out capacityInBytes);

                // Cast to float since the data we are generating is float
                dataInFloat = (float*)dataInBytes;

                float amplitude = 0.3f;
                int sampleRate = (int)graph.EncodingProperties.SampleRate;
                double sampleIncrement = (freq * (Math.PI * 2)) / sampleRate;

                // Generate a 1kHz sine wave and populate the values in the memory buffer
                for (int i = 0; i < samplesNeeded; i++)
                {
                    double sinValue = amplitude * Math.Sin(theta);
                    dataInFloat[i] = (float)sinValue;
                    theta += sampleIncrement;
                }
            }

            return frame;
        }

        int notesPlaying = 0;

        private void Port_MessageReceived(MidiInPort sender, MidiMessageReceivedEventArgs args)
        {
            if (args.Message.Type == MidiMessageType.NoteOn)
            {
                var msg = (MidiNoteOnMessage)args.Message;

                if (msg.Note != 0)
                {
                    view.Title = $"Pitch: {msg.Note}, Velocity: {msg.Velocity}";

                    if (msg.Velocity != 0)
                    {
                        notesPlaying++;

                        freq = notes[msg.Note];
                        theta = 0;

                        input.Start();
                    }
                    else
                    {
                        notesPlaying--;

                        if (notesPlaying == 0)
                            input.Stop();
                    }
                }
            }
        }
    }
}
