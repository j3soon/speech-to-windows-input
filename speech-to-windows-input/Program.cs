using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using System;
using System.IO;
using System.Media;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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
    }
    public class SpeechRecognitionResult
    {
        public String Text = null;
        public String ErrorMessage = null;
    }
    class Program
    {
        static Config config = new Config();
        static llc.KeyboardHook kbdHook = new llc.KeyboardHook();
        static SpeechRecognizer speechRecognizer;
        static bool loop = true; // mutex isn't necessary since both Main and Application.DoEvents (WinProc) is in the main thread.
        static bool keyHDown = false;
        static Task<SpeechRecognitionResult> task = null;
        // static void 
        static void Main(string[] args)
        {
            // Tutorial
            Console.OutputEncoding = System.Text.Encoding.Unicode;
            Console.WriteLine("speech-to-windows-input (made by j3soon)");
            Console.WriteLine("1. Press Win+H to convert speech to text input. The recognition stops on microphone silence or after 15 seconds.");
            // Console.WriteLine("2. Press ESC to cancel the on-going speech recognition (no input will be generated).");
            Console.WriteLine("2. The on-going speech recognition cannot be cancelled. (please wait until 15 seconds is reached)");
            Console.WriteLine("3. Press Ctrl+C to exit.");
            Console.WriteLine("Notes:");
            Console.WriteLine("- Requires internet.");
            Console.WriteLine("- The default microphone is used for speech recognition.");
            Console.WriteLine("- If input fails for certain applications, you may need to launch this program with `Run as administrator`.");
            // Generate and Load Config
            var jsonConfig = JsonSerializer.Serialize(config, new JsonSerializerOptions {
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
            // Message Loop with Windows.Forms for simplicity (instead of custom WindowProc callback)
            while (loop)
            {
                // Deal with Low Level Hooks Callback
                Application.DoEvents();
                // Deal with Speech Recognition Result
                if (task != null && task.IsCompleted)
                {
                    if (task.Result.ErrorMessage == null)
                    {
                        Console.WriteLine($"Recognized Text: {task.Result.Text}");
                        llc.Keyboard.SendText(task.Result.Text);
                    }
                    else
                    {
                        Console.WriteLine($"Error: {task.Result.ErrorMessage}");
                    }
                    task = null;
                    Console.WriteLine($"[{DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss")}] Speech Recognition Done");
                    if (config.SoundEffect)
                        SystemSounds.Exclamation.Play();
                }
                Thread.Sleep(1);
            }
            // Uninstall Ketboard Hook
            kbdHook.UninstallGlobalHook();
        }
        private static void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            loop = false;
        }
        private static bool kbdHook_KeyDownEvent(llc.KeyboardHook sender, uint vkCode, bool injected)
        {
            if (vkCode == (uint)Keys.H && !keyHDown)
            {
                keyHDown = true;
                if (llc.Keyboard.IsKeyDown((int)Keys.LWin) || llc.Keyboard.IsKeyDown((int)Keys.RWin)) {
                    if (task != null)
                        return false;
                    if (task == null)
                    {
                        Console.WriteLine($"[{DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss")}] Speech Recognition Started");
                        if (config.SoundEffect)
                            SystemSounds.Exclamation.Play();
                        task = SpeechToTextAsync();
                    }
                    return true;
                }
            }
            /*else if (vkCode == (uint)Keys.Escape)
            {
                if (task != null)
                {
                    cancelled = true;
                    Console.WriteLine($"[{DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss")}] Speech Recognition Cancelled");
                    return true;
                }
            }*/
            return false;
        }
        private static bool kbdHook_KeyUpEvent(llc.KeyboardHook sender, uint vkCode, bool injected)
        {
            if (vkCode == (uint)Keys.H)
                keyHDown = false;
            return false;
        }
        private static void InitSpeechRecognizer()
        {
            // Creates an instance of a speech config with specified subscription key and service region.
            var speechConfig = SpeechConfig.FromSubscription(config.AzureSubscriptionKey, config.AzureServiceRegion);
            speechConfig.SetProperty(PropertyId.SpeechServiceConnection_SingleLanguageIdPriority, config.PrioritizeLatencyOrAccuracy);
            var autoDetectSourceLanguageConfig = AutoDetectSourceLanguageConfig.FromLanguages(config.Languages);
            var audioConfig = AudioConfig.FromDefaultMicrophoneInput();
            speechRecognizer = new SpeechRecognizer(speechConfig, autoDetectSourceLanguageConfig, audioConfig);
            PhraseListGrammar phraseListGrammar = PhraseListGrammar.FromRecognizer(speechRecognizer);
            foreach (var phrase in config.PhraseList)
                phraseListGrammar.AddPhrase(phrase);
        }
        private static async Task<SpeechRecognitionResult> SpeechToTextAsync()
        {
            // Starts speech recognition, and returns after a single utterance is recognized. The end of a
            // single utterance is determined by listening for silence at the end or until a maximum of 15
            // seconds of audio is processed.  The task returns the recognition text as result. 
            // Note: Since RecognizeOnceAsync() returns only a single utterance, it is suitable only for single
            // shot recognition like command or query. 
            // For long-running multi-utterance recognition, use StartContinuousRecognitionAsync() instead.
            var result = await speechRecognizer.RecognizeOnceAsync();
            var ret = new SpeechRecognitionResult();

            // Checks result.
            if (result.Reason == ResultReason.RecognizedSpeech)
            {
                ret.Text = result.Text;
            }
            else if (result.Reason == ResultReason.NoMatch)
            {
                ret.ErrorMessage = $"NOMATCH: Speech could not be recognized.";
            }
            else if (result.Reason == ResultReason.Canceled)
            {
                var cancellation = CancellationDetails.FromResult(result);
                ret.ErrorMessage = $"CANCELED: Reason={cancellation.Reason}";

                if (cancellation.Reason == CancellationReason.Error)
                {
                    ret.ErrorMessage += $"\nCANCELED: ErrorCode={cancellation.ErrorCode}";
                    ret.ErrorMessage += $"\nCANCELED: ErrorDetails={cancellation.ErrorDetails}";
                    ret.ErrorMessage += $"\nCANCELED: Did you update the subscription info?";
                }
            }
            return ret;
        }
    }
}

