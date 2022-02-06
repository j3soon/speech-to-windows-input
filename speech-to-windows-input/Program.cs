using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Media;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;
using llc = LowLevelControls;

namespace speech_to_windows_input
{
    public class Config
    {
        public String AzureSubscriptionKey { get; set; } = "<paste-your-subscription-key>";
        public String AzureServiceRegion { get; set; } = "<paste-your-region>"; // (e.g., "westus" or "eastasia")
        // All languages ref: https://docs.microsoft.com/en-us/azure/cognitive-services/speech-service/language-support#speech-to-text
        public String[] Languages { get; set; } = { "en-US", "zh-TW" };
        public String[] PhraseList { get; set; } = { };
        public String PrioritizeLatencyOrAccuracy { get; set; } = "Latency";
        public bool SoundEffect { get; set; } = false;
        public bool InputIncrementally { get; set; } = true;
        public String OutputForm { get; set; } = "Text"; // or "Lexical", or "Normalized"
        public bool AutoPunctuation { get; set; } = true;
        public bool DetailedLog { get; set; } = false;
        public bool ContinuousRecognition { get; set; } = false;
        public int TotalTimeoutMS { get; set; } = 60000;
        public bool UseMenuKey { get; set; } = false;
        // TODO: Send Enter after recognition
    }
    class Program
    {
        static Config config = new Config();
        static llc.KeyboardHook kbdHook = new llc.KeyboardHook();
        static SpeechRecognizer speechRecognizer;
        static bool loop = true; // mutex isn't necessary since both Main and Application.DoEvents (WinProc) is in the main thread.
        static bool keyHDown = false;
        static bool keyAppsDown = false;
        static bool recognizing = false;
        static bool cancelling = false;
        static String partialRecognizedText = "";
        static Stopwatch stopwatch = null;
        static ConcurrentQueue<Tuple<String, String>> inputQueue = new ConcurrentQueue<Tuple<String, String>>();
        // static void 
        static void Main(string[] args)
        {
            // Tutorial
            Console.OutputEncoding = System.Text.Encoding.Unicode;
            Console.WriteLine("speech-to-windows-input (made by j3soon)");
            Console.WriteLine("1. Press Alt+H to convert speech to text input. The recognition stops on (1) microphone silence (2) after 15 seconds (3) Alt+H is pressed again.");
            Console.WriteLine("2. Press ESC to cancel the on-going speech recognition (no input will be generated).");
            Console.WriteLine("3. Press Ctrl+C to exit.");
            Console.WriteLine("Notes:");
            Console.WriteLine("- Requires internet.");
            Console.WriteLine("- The default microphone is used for speech recognition.");
            Console.WriteLine("- If input fails for certain applications, you may need to launch this program with `Run as administrator`.");
            // Generate and Load Config
            var jsonConfig = JsonSerializer.Serialize(config, new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                WriteIndented = true,
            });
            var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
            if (!File.Exists(configPath))
                File.WriteAllText(configPath, jsonConfig);
            jsonConfig = File.ReadAllText(configPath);
            Console.WriteLine("Your current configuration: " + jsonConfig);
            try
            {
                config = JsonSerializer.Deserialize<Config>(jsonConfig);
            }
            catch (JsonException e)
            {
                Console.WriteLine(e.ToString());
                Console.WriteLine("\nError occurred when parsing JSON config, press any key to exit.");
                Console.ReadKey();
                return;
            }
            // Install Keyboard Hook and Callbacks
            kbdHook.KeyDownEvent += kbdHook_KeyDownEvent;
            kbdHook.KeyUpEvent += kbdHook_KeyUpEvent;
            kbdHook.InstallGlobalHook();
            Console.CancelKeyPress += Console_CancelKeyPress;
            // Init Speech Recognizer
            InitSpeechRecognizer();
            Thread thread = new Thread(SendInput);
            thread.Start();
            // Message Loop with Windows.Forms for simplicity (instead of custom WindowProc callback)
            while (loop)
            {
                Application.DoEvents(); // Deal with Low Level Hooks Callback
                if (stopwatch != null && stopwatch.ElapsedMilliseconds >= config.TotalTimeoutMS)
                {
                    // stopwatch != null means:
                    // - continuousRecognition == true
                    // - recognizing == true
                    stopwatch.Stop();
                    stopwatch = null;
                    speechRecognizer.StopContinuousRecognitionAsync();
                }
                Thread.Sleep(1);
            }
            thread.Abort();
            // Uninstall Ketboard Hook
            kbdHook.UninstallGlobalHook();
        }
        private static void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            loop = false;
        }
        private static void ToggleSTT()
        {
            if (!recognizing)
            {
                Console.WriteLine($"[{DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss")}] Speech Recognition Started");
                if (config.SoundEffect)
                    SystemSounds.Exclamation.Play();
                cancelling = false;
                recognizing = true;
                partialRecognizedText = "";
                if (!config.ContinuousRecognition)
                {
                    // Starts speech recognition, and returns after a single utterance is recognized. The end of a
                    // single utterance is determined by listening for silence at the end or until a maximum of 15
                    // seconds of audio is processed.  The task returns the recognition text as result. 
                    // Note: Since RecognizeOnceAsync() returns only a single utterance, it is suitable only for single
                    // shot recognition like command or query. 
                    // For long-running multi-utterance recognition, use StartContinuousRecognitionAsync() instead.
                    speechRecognizer.RecognizeOnceAsync();
                }
                else
                {
                    stopwatch = new Stopwatch();
                    stopwatch.Start();
                    speechRecognizer.StartContinuousRecognitionAsync();
                }
            }
            else
            {
                Console.WriteLine($"[{DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss")}] Speech Recognition Early Stopping...");
                speechRecognizer.StopContinuousRecognitionAsync();
            }
        }
        private static void CancelSTT()
        {
            if (recognizing)
            {
                cancelling = true;
                Console.WriteLine($"[{DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss")}] Speech Recognition Cancelling...");
                speechRecognizer.StopContinuousRecognitionAsync();
            }
        }
        private static bool kbdHook_KeyDownEvent(llc.KeyboardHook sender, uint vkCode, bool injected)
        {
            if (vkCode == (uint)Keys.H && !keyHDown)
            {
                keyHDown = true;
                if (llc.Keyboard.IsKeyDown((int)Keys.Menu))
                {
                    ToggleSTT();
                    return true;
                }
            }
            else if (vkCode == (uint)Keys.Apps && !keyAppsDown)
            {
                if (config.UseMenuKey)
                {
                    keyAppsDown = true;
                    ToggleSTT();
                    return true;
                }
            }
            else if (vkCode == (uint)Keys.Escape)
            {
                if (recognizing)
                {
                    CancelSTT();
                    return true;
                }
            }
            return false;
        }
        private static bool kbdHook_KeyUpEvent(llc.KeyboardHook sender, uint vkCode, bool injected)
        {
            if (vkCode == (uint)Keys.H && keyHDown)
                keyHDown = false;
            else if (vkCode == (uint)Keys.Apps && keyAppsDown)
            {
                if (config.UseMenuKey)
                {
                    keyAppsDown = false;
                    return true;
                }
            }
            return false;
        }
        private static String GetCommonPrefix(String s1, String s2)
        {
            int i, min = Math.Min(s1.Length, s2.Length);
            for (i = 0; i < min && s1[i] == s2[i]; i++) { }
            return s1.Substring(0, i);
        }
        private static void QueueInput(String text)
        {
            if (cancelling)
                return;
            inputQueue.Enqueue(new Tuple<String, String>(partialRecognizedText, text));
        }
        private static void SendInput()
        {
            while (true)
            {
                Thread.Sleep(1); // Change busy-waiting to sleep & wake-up
                Tuple<String, String> tuple;
                if (!inputQueue.TryDequeue(out tuple))
                    continue;
                // Note: text may be longer/shorter than partialRecognizedText
                String s = GetCommonPrefix(tuple.Item1, tuple.Item2);
                for (int i = 0; i < tuple.Item1.Length - s.Length; i++)
                    llc.Keyboard.SendKeyDown((int)Keys.Back);
                if (tuple.Item1.Length - s.Length > 0)
                    llc.Keyboard.SendKeyUp((int)Keys.Back);
                if (s.Length < tuple.Item2.Length)
                    llc.Keyboard.SendText(tuple.Item2.Substring(s.Length));
            }
        }
        private static void InitSpeechRecognizer()
        {
            // Creates an instance of a speech config with specified subscription key and service region.
            var speechConfig = SpeechConfig.FromSubscription(config.AzureSubscriptionKey, config.AzureServiceRegion);
            speechConfig.SetProperty(PropertyId.SpeechServiceConnection_SingleLanguageIdPriority, config.PrioritizeLatencyOrAccuracy);
            // Output detailed recognition results
            speechConfig.OutputFormat = OutputFormat.Detailed;
            // Don't filter bad words
            speechConfig.SetProfanity(ProfanityOption.Raw);
            if (!config.AutoPunctuation)
                // Preview feature, mentioned here: https://github.com/Azure-Samples/cognitive-services-speech-sdk/issues/667#issuecomment-690840772
                // Don't automatically insert punctuations
                speechConfig.SetServiceProperty("punctuation", "explicit", ServicePropertyChannel.UriQueryParameter);
            var autoDetectSourceLanguageConfig = AutoDetectSourceLanguageConfig.FromLanguages(config.Languages);
            var audioConfig = AudioConfig.FromDefaultMicrophoneInput();
            speechRecognizer = new SpeechRecognizer(speechConfig, autoDetectSourceLanguageConfig, audioConfig);
            PhraseListGrammar phraseListGrammar = PhraseListGrammar.FromRecognizer(speechRecognizer);
            foreach (var phrase in config.PhraseList)
                phraseListGrammar.AddPhrase(phrase);
            speechRecognizer.Recognizing += (s, e) =>
            {
                Console.WriteLine($"Partial Recognized Text: {e.Result.Text}");
                if (!config.InputIncrementally)
                    return;
                QueueInput(e.Result.Text);
                partialRecognizedText = e.Result.Text;
            };
            speechRecognizer.Recognized += (s, e) =>
            {
                var result = e.Result;
                String Text = null;
                if (config.DetailedLog)
                    Console.WriteLine($"Detailed Result: {result}");
                // Checks result.
                if (result.Reason == ResultReason.RecognizedSpeech)
                {
                    var detailedResult = result.Best().First();
                    if (config.OutputForm == "Text")
                        Text = result.Text;
                    else if (config.OutputForm == "Lexical")
                        Text = detailedResult.LexicalForm;
                    else if (config.OutputForm == "Normalized")
                        Text = detailedResult.NormalizedForm;
                    else
                    {
                        var msg = $"CONFIG ERROR: OutputForm cannot be set to: \"{config.OutputForm}\"";
                        Console.WriteLine(msg);
                        Text = msg;
                    }
                }
                else if (result.Reason == ResultReason.NoMatch)
                {
                    Console.WriteLine($"NOMATCH: Speech could not be recognized.");
                    // if (config.StopOnSilence)
                    //     speechRecognizer.StopContinuousRecognitionAsync();
                }
                else if (result.Reason == ResultReason.Canceled)
                {
                    var cancellation = CancellationDetails.FromResult(result);
                    Console.WriteLine($"CANCELED: Reason={cancellation.Reason}");

                    if (cancellation.Reason == CancellationReason.Error)
                    {
                        Console.WriteLine($"CANCELED: ErrorCode={cancellation.ErrorCode}");
                        Console.WriteLine($"CANCELED: ErrorDetails={cancellation.ErrorDetails}");
                        Console.WriteLine($"CANCELED: Did you update the subscription info?");
                    }
                }
                if (Text != null)
                {
                    Console.WriteLine($"Final Recognized Text: {Text}");
                    QueueInput(Text);
                    partialRecognizedText = "";
                }
            };
            speechRecognizer.Canceled += (s, e) =>
            {
                Console.WriteLine($"CANCELED: Reason={e.Reason}");

                if (e.Reason == CancellationReason.Error)
                {
                    Console.WriteLine($"CANCELED: ErrorCode={e.ErrorCode}");
                    Console.WriteLine($"CANCELED: ErrorDetails={e.ErrorDetails}");
                    Console.WriteLine($"CANCELED: Did you update the speech key and location/region info?");
                }
                // Re-initialize to fix temporary internet issue.
                InitSpeechRecognizer();
            };
            speechRecognizer.SessionStopped += (s, e) =>
            {
                recognizing = false;
                if (config.SoundEffect)
                    SystemSounds.Exclamation.Play();
                if (!cancelling)
                    Console.WriteLine($"[{DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss")}] Speech Recognition Done");
                else
                    Console.WriteLine($"[{DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss")}] Speech Recognition Cancelled");
            };
            Console.WriteLine($"[{DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss")}] Speech Recognizer Initialized");
        }
    }
}

