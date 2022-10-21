using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
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
        public int UseFxKey { get; set; } = 0;
        public bool SendTrailingEnter { get; set; } = false;
        public bool SendTrailingSpace { get; set; } = false;
        public bool ChineseChatMode { get; set; } = false;
        public bool ForceCapitalizeFirstAlphabet { get; set; } = true;
        public bool ShowListeningOverlay { get; set; } = true;
        public bool UseSwitchConfigKey { get; set; } = false;
    }
    class Program
    {
        static Config config = new Config();
        static llc.KeyboardHook kbdHook = new llc.KeyboardHook();
        static SpeechRecognizer speechRecognizer;
        static bool loop = true; // mutex isn't necessary since both Main and Application.DoEvents (WinProc) is in the main thread.
        static bool keyHDown = false;
        static bool keyAppsDown = false;
        static bool keyFxDown = false;
        static bool keyConfigDown = false;
        static bool recognizing = false;
        static bool shouldReloadConfig = false;
        static bool cancelling = false;
        static uint configId = 0;
        static String partialRecognizedText = "";
        static Stopwatch stopwatch = null;
        static String version = "%VERSION_STRING%";
        static ConcurrentQueue<Tuple<String, String>> inputQueue = new ConcurrentQueue<Tuple<String, String>>();
        static Form1 form1;
        static void Main(string[] args)
        {
            try
            {
                // Mutex lock for running only one instance of the program
                var assembly = typeof(Program).Assembly;
                var attribute = (GuidAttribute)assembly.GetCustomAttributes(typeof(GuidAttribute), true)[0];
                var fvi = System.Diagnostics.FileVersionInfo.GetVersionInfo(assembly.Location);
                version = fvi.FileVersion;
                version = version.Remove(version.Length - 2);  // Remove the trailing ".0"
                var guid = attribute.Value;
                // The mutex will be released by the OS when application exit / crash
                var mutex = new Mutex(true, guid);
                if (!mutex.WaitOne(TimeSpan.Zero, true))
                {
                    Console.WriteLine("Another Instance of this Program is Already Running.");
                    Console.WriteLine("Press any key to exit.");
                    Console.ReadKey();
                    return;
                }
                form1 = new Form1();
                // Tutorial
                Console.OutputEncoding = System.Text.Encoding.Unicode;
                Console.WriteLine("speech-to-windows-input (STWI) v" + version + "\n");
                Console.WriteLine("Source Code Link (MIT License):\n");
                Console.WriteLine("    https://github.com/j3soon/speech-to-windows-input \n");
                Console.WriteLine("1. Press Alt+H to convert speech to text input. The recognition stops on (1) microphone silence (2) after 15 seconds (3) Alt+H is pressed again.");
                Console.WriteLine("2. Press ESC to cancel the on-going speech recognition (no input will be generated).");
                Console.WriteLine("3. Press Ctrl+C to exit.");
                Console.WriteLine("");
                Console.WriteLine("Notes:");
                Console.WriteLine("- The default microphone & internet connection is used for speech recognition.");
                Console.WriteLine("- If input fails for certain applications, you may need to launch this program with `Run as administrator`.");
                Console.WriteLine("- The initial recognition delay is for detecting the language used. You can modify the language list to contain only a single language to speed up the process.");
                Console.WriteLine("");
                // Generate and Load Config
                if (!LoadConfig())
                {
                    Console.WriteLine("Press any key to exit.");
                    Console.ReadKey();
                    return;
                }
                // Install Keyboard Hook and Callbacks
                kbdHook.KeyDownEvent += kbdHook_KeyDownEvent;
                kbdHook.KeyUpEvent += kbdHook_KeyUpEvent;
                kbdHook.InstallGlobalHook();
                Console.CancelKeyPress += Console_CancelKeyPress;
                // Set up watcher for config hot reload
                var watcher = new FileSystemWatcher(AppDomain.CurrentDomain.BaseDirectory);
                watcher.NotifyFilter = NotifyFilters.LastWrite;
                watcher.Filter = "config*.json";
                watcher.Changed += (s, e) =>
                {
                    if (e.Name !=getConfigFilename())
                        return;
                    // May fire twice when using Notepad
                    // See https://stackoverflow.com/q/1764809/3917161
                    Console.WriteLine($"[{DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss")}] Config File is Modified, Reloading...");
                    shouldReloadConfig = true;
                };
                watcher.EnableRaisingEvents = true;
                // Init Speech Recognizer
                InitSpeechRecognizer();
                Thread thread = new Thread(SendInputWorker);
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
                    if (!recognizing && shouldReloadConfig)
                    {
                        shouldReloadConfig = false;
                        if (LoadConfig())
                        {
                            var suffix = (configId == 0 ? "" : configId.ToString());
                            Console.WriteLine($"[{DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss")}] Config File Reloaded. ({getConfigFilename()})");
                        }
                        else
                        {
                            Console.WriteLine($"[{DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss")}] Config File Failed to Reload, Using Old Config.");
                        }
                        InitSpeechRecognizer();
                    }
                    Thread.Sleep(1);
                }
                thread.Abort();
                mutex.ReleaseMutex();
            }
            catch (Exception e)
            {
                ReportError(e.Message, e.ToString());
            }
            finally
            {
                // If the keyboard hook isn't uninstalled here when an error occurs, the user's keyboard will lag until program exit.
                if (kbdHook.hookInstalled)
                {
                    // Uninstall Ketboard Hook
                    kbdHook.UninstallGlobalHook();
                }
            }
            Console.ReadLine();
        }

        static void ReportError(String message, String description, bool fatal=true)
        {
            var banner = new String('=', 35);
            var prefix = fatal ? " FATAL" : "";
            Console.WriteLine("\n" + banner + prefix + " ERROR! " + banner + "\n");
            if (!fatal)
            {
                // Error during recognition, not fatal.
                Console.WriteLine("The author (j3soon) thinks that the issue may be:\n");
                Console.Write("    - You didn't fill in your subscription info (`AzureSubscriptionKey` and `AzureServiceRegion`) in `config.json` after downloading the program. ");
                Console.WriteLine("Or you used up all your quota, or the keys have expired.");
                Console.WriteLine("    - Your internet connection dropped, or the connection is unstable.");
                Console.WriteLine("    - Your microphone is unplugged, or the connection is unstable.\n");
            }
            else
            {
                Console.WriteLine(message);
                Console.WriteLine(description);
                Console.WriteLine("speech-to-windows-input (STWI) v" + version + "\n");
                Console.Write("A fatal error occured. ");
                if (message.Contains("SPXERR_MIC_NOT_AVAILABLE"))
                {
                    Console.WriteLine("The author (j3soon) thinks that the issue may be:\n");
                    Console.WriteLine("    You didn't plugin a microphone. Or you system cannot detect your microphone.\n");
                }
                else if (message.Contains("Unable to load DLL"))
                {
                    Console.WriteLine("The author (j3soon) thinks that the issue may be:\n");
                    Console.WriteLine("    The program is not able to locate the required `DLL` files. You may accidentally moved `speech-to-windows-input.exe` outside its folder. Please backup your `config.json` file and re-download the program:\n");
                    Console.WriteLine("    https://github.com/j3soon/speech-to-windows-input/releases \n");
                }
                else if (message.Contains("Custom Exception thrown by the author"))
                {
                    Console.WriteLine("The author (j3soon) thinks that the issue may be:\n");
                    Console.WriteLine("    You didn't fill in your subscription info (`AzureSubscriptionKey` and `AzureServiceRegion`) in `config.json` after downloading the program.\n");
                }
            }
            Console.WriteLine("If you cannot resolve the problem by yourself, please open an issue and provide the error message and other details:\n");
            Console.WriteLine("    https://github.com/j3soon/speech-to-windows-input/issues \n");
            if (fatal)
                Console.WriteLine("Press ENTER to exit.");
        }

        static String getConfigFilename()
        {
            var suffix = (configId == 0 ? "" : configId.ToString());
            return $"config{suffix}.json";
        }
        static bool LoadConfig()
        {
            var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, getConfigFilename());
            var jsonConfig = JsonSerializer.Serialize(config, new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                WriteIndented = true,
            });
            if (!File.Exists(configPath))
                File.WriteAllText(configPath, jsonConfig);
            jsonConfig = File.ReadAllText(configPath);
            try
            {
                config = JsonSerializer.Deserialize<Config>(jsonConfig);
            }
            catch (JsonException e)
            {
                Console.WriteLine(e.ToString());
                Console.WriteLine("\nError occurred when parsing JSON config.");
                return false;
            }
            // Serialize again to remove invalid config
            jsonConfig = JsonSerializer.Serialize(config, new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                WriteIndented = true,
            });
            Console.WriteLine("Your current configuration: " + jsonConfig + "\n");
            if (config.AzureSubscriptionKey == "<paste-your-subscription-key>")
                throw new Exception("Custom Exception thrown by the author: `AzureSubscriptionKey` is not set");
            if (config.AzureServiceRegion == "<paste-your-region>")
                throw new Exception("Custom Exception thrown by the author: `AzureServiceRegion` is not set");
            return true;
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
                if (config.ShowListeningOverlay)
                {
                    // The window doesn't show up sometimes (don't know how to reproduce) if we only change visibility as below
                    // form1.Visible = true;

                    // Try to make sure the window does show up in all scenarios
                    form1.WindowState = FormWindowState.Normal;
                    form1.Show();
                    form1.BringToFront();
                }
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
                if (llc.Keyboard.IsKeyDown((int)Keys.Menu))
                {
                    keyHDown = true;
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
            else if (vkCode >= (uint)Keys.F1 && vkCode <= (uint)Keys.F24 && !keyFxDown)
            {
                if (config.UseFxKey == vkCode + 1 - (uint)Keys.F1)
                {
                    keyFxDown = true;
                    ToggleSTT();
                    return true;
                }
            }
            else if (vkCode >= (uint)Keys.D0 && vkCode <= (uint)Keys.D9 && !keyConfigDown)
            {
                if (config.UseSwitchConfigKey && llc.Keyboard.IsKeyDown((int)Keys.Menu))
                {
                    keyConfigDown = true;
                    configId = (vkCode - (uint)Keys.D0);
                    shouldReloadConfig = true;
                    return true;
                }
            }
            else if (vkCode == (uint)Keys.Escape && !injected)
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
            {
                keyHDown = false;
                if (!llc.Keyboard.IsKeyDown((int)Keys.Menu))
                    llc.Keyboard.SendKey((int)Keys.LMenu);
            }
            else if (vkCode == (uint)Keys.Apps && keyAppsDown)
            {
                if (config.UseMenuKey)
                {
                    keyAppsDown = false;
                    return true;
                }
            }
            else if (vkCode >= (uint)Keys.F1 && vkCode <= (uint)Keys.F24 && keyFxDown)
            {
                if (config.UseFxKey == vkCode + 1 - (uint)Keys.F1)
                {
                    keyFxDown = false;
                    return true;
                }
            }
            else if (vkCode >= (uint)Keys.D0 && vkCode <= (uint)Keys.D9 && keyConfigDown)
            {
                keyConfigDown = false;
                if (!llc.Keyboard.IsKeyDown((int)Keys.Menu))
                    llc.Keyboard.SendKey((int)Keys.LMenu);
            }
            return false;
        }
        private static String GetCommonPrefix(String s1, String s2)
        {
            int i, min = Math.Min(s1.Length, s2.Length);
            for (i = 0; i < min && s1[i] == s2[i]; i++) { }
            return s1.Substring(0, i);
        }
        private static String PostProcessText(String text)
        {
            if (text == null)
                return null;
            if (text.Length == 0)
                return text;
            if (config.ChineseChatMode)
                text = text.Replace("，", " ").Replace("。", "");
            if (config.ForceCapitalizeFirstAlphabet)
                text = text[0].ToString().ToUpper() + text.Substring(1);
            return text;
        }
        private static void QueueInput(String text)
        {
            if (cancelling)
                return;
            inputQueue.Enqueue(new Tuple<String, String>(PostProcessText(partialRecognizedText), PostProcessText(text)));
        }
        // Copied directly from llc
        private static llc.Natives.INPUT getInput(int key, bool down, bool unicode = false)
        {
            llc.Natives.INPUT input = new llc.Natives.INPUT
            {
                type = llc.Natives.INPUTTYPE.INPUT_KEYBOARD,
            };
            if (unicode)
            {
                input.mkhi.ki = new llc.Natives.KEYBDINPUT
                {
                    wVk = 0,
                    wScan = (ushort)key,
                    dwFlags = (uint)llc.Natives.KEYEVENTF.UNICODE | (uint)(down ? llc.Natives.KEYEVENTF.KEYDOWN : llc.Natives.KEYEVENTF.KEYUP),
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                };
                return input;
            }
            input.mkhi.ki = new llc.Natives.KEYBDINPUT
            {
                wVk = (ushort)key,
                wScan = 0,
                dwFlags = (uint)(down ? llc.Natives.KEYEVENTF.KEYDOWN : llc.Natives.KEYEVENTF.KEYUP),
                time = 0,
                dwExtraInfo = IntPtr.Zero
            };
            return input;
        }
        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint nInputs, llc.Natives.INPUT[] pInputs, int cbSize);
        // Pack before send can reduce the number of system calls and speed up the SendInput process.
        private static void SendBackspaceAndText(int backSpaces, String text)
        {
            int len = 2 * backSpaces + 2 * text.Length;
            var inputs = new llc.Natives.INPUT[len];
            int textBegin = 2 * backSpaces;
            for (int i = 0; i < backSpaces; i++)
            {
                inputs[2 * i] = getInput((int)Keys.Back, true);
                inputs[2 * i + 1] = getInput((int)Keys.Back, false);
            }
            for (int i = 0; i < text.Length; i++)
            {
                inputs[textBegin + 2 * i] = getInput(text[i], true, true);
                inputs[textBegin + 2 * i + 1] = getInput(text[i], false, true);
            }
            uint sent = SendInput((uint)len, inputs, Marshal.SizeOf(typeof(llc.Natives.INPUT)));
            if (sent != len)
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        private static void SendInputWorker()
        {
            while (true)
            {
                Thread.Sleep(1); // Change busy-waiting to sleep & wake-up
                Tuple<String, String> tuple;
                if (!inputQueue.TryDequeue(out tuple))
                    continue;
                // Note: text may be longer/shorter than partialRecognizedText
                if (tuple.Item2 == null)
                {
                    // Null Item2 only occurs for signaling enter.
                    if (config.SendTrailingSpace)
                        llc.Keyboard.SendKey((int)Keys.Space);
                    if (config.SendTrailingEnter)
                        llc.Keyboard.SendKey((int)Keys.Enter);
                    continue;
                }
                String s = GetCommonPrefix(tuple.Item1, tuple.Item2);
                int backSpaces = 0;
                String text = "";
                if (s.Length < tuple.Item2.Length)
                    text = tuple.Item2.Substring(s.Length);
                if (tuple.Item1.Length - s.Length > 0)
                    backSpaces = tuple.Item1.Length - s.Length;
                SendBackspaceAndText(backSpaces, text);
            }
        }
        private static void InitSpeechRecognizer()
        {
            // Creates an instance of a speech config with specified subscription key and service region.
            var speechConfig = SpeechConfig.FromSubscription(config.AzureSubscriptionKey, config.AzureServiceRegion);
            speechConfig.SetProperty(PropertyId.SpeechServiceConnection_SingleLanguageIdPriority, config.PrioritizeLatencyOrAccuracy);
            // Output detailed recognition results
            speechConfig.OutputFormat = OutputFormat.Detailed;
            // Don't filter bad wor s
            speechConfig.SetProfanity(ProfanityOption.Raw);
            if (!config.AutoPunctuation)
                // Preview feature, mentioned here: https://github.com/Azure-Samples/cognitive-services-speech-sdk/issues/667#issuecomment-690840772
                // Don't automatically insert punctuations
                speechConfig.SetServiceProperty("punctuation", "explicit", ServicePropertyChannel.UriQueryParameter);
            if (config.Languages.Length == 1)
            {
                speechConfig.SpeechRecognitionLanguage = config.Languages[0];
                speechRecognizer = new SpeechRecognizer(speechConfig);
            }
            else
            {
                var autoDetectSourceLanguageConfig = AutoDetectSourceLanguageConfig.FromLanguages(config.Languages);
                var audioConfig = AudioConfig.FromDefaultMicrophoneInput();
                speechRecognizer = new SpeechRecognizer(speechConfig, autoDetectSourceLanguageConfig, audioConfig);
            }
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
                        ReportError(cancellation.ErrorCode.ToString(), cancellation.ErrorDetails, false);
                    }
                }
                if (Text != null)
                {
                    Console.WriteLine($"Final Recognized Text: {Text}");
                    QueueInput(Text);
                    partialRecognizedText = "";
                    if (Text != "")
                        QueueInput(null); // Signal end of recognition
                }
            };
            speechRecognizer.Canceled += (s, e) =>
            {
                Console.WriteLine($"CANCELED: Reason={e.Reason}");

                if (e.Reason == CancellationReason.Error)
                {
                    Console.WriteLine($"CANCELED: ErrorCode={e.ErrorCode}");
                    Console.WriteLine($"CANCELED: ErrorDetails={e.ErrorDetails}");
                    ReportError(e.ErrorCode.ToString(), e.ErrorDetails, false);
                }
                // Re-initialize to fix temporary internet issue.
                InitSpeechRecognizer();
            };
            speechRecognizer.SessionStopped += (s, e) =>
            {
                recognizing = false;
                if (config.ShowListeningOverlay)
                    form1.Invoke((MethodInvoker)delegate { form1.Hide(); }); // Thread safe calls to control
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

