# Speech to Windows Input (STWI)

Perform speech-to-text (STT/ASR) with Azure Speech service and simulate keyboard to input the recognized the text.

Supports English, Chinese, Japanese, [and more...](https://docs.microsoft.com/en-us/azure/cognitive-services/speech-service/language-support#speech-to-text)

## Why this Project?

- Windows 10 [Speech Recognition](https://support.microsoft.com/en-au/windows/use-voice-typing-to-talk-instead-of-type-on-your-pc-fec94565-c4bd-329d-e59a-af033fa5689f) (``Win+H``) only supports English.
- Although the old [Speech Recognition](https://www.windowscentral.com/how-set-speech-recognition-windows-10) tool in Windows 10 supports Chinese and more languages, the recognition accuracy is very bad.
- Windows 11 [Speech Recognition](https://support.microsoft.com/en-au/windows/use-voice-typing-to-talk-instead-of-type-on-your-pc-fec94565-c4bd-329d-e59a-af033fa5689f#WindowsVersion=Windows_11) (``Win+H``) supports more languages, but seems to have lower English accuracy compared to the Azure Speech Service used in this program.
- [Office 365 Speech Recognition](https://support.microsoft.com/en-us/office/dictate-your-documents-in-word-3876e05f-3fcc-418f-b8ab-db7ce0d11d3c) (``Alt+` ``) supports English, Chinese, Japanese and more languages. The recognition accuracy (especially for Chinese) is really good. Unfortunately, it only works in Office 365 applications and requires an Office 365 subscription. (Not free)
- Microsoft didn't implement the Office 365 Speech Recognition function in Windows.

So I decided to implement a free & open-source speech recognition tool that works on all applications on Windows.

## Why Use Azure Speech Service as Backend?

- High recognition accuracy as in Office 365 Speech Recognition. (The accuracy seems to be better than Google's speech service)
- Offline speech recognition model is not practical due to the hardware requirements. (Also, I don't know how to train an ASR model as accurate as Azure's Speech Service)
- The free tier has [5 hours quota each month](https://azure.microsoft.com/en-us/pricing/details/cognitive-services/speech-services/), which is sufficient for normal usages. You won't be [charged by exceeding the usage quota in the free tier](https://azure.microsoft.com/en-us/free/free-account-faq/). Moreover, new users receive [\$200 free credits](https://azure.microsoft.com/en-us/offers/ms-azr-0044p/), which means a total of 200 hrs ($1 per hour).
- The only drawback is requiring a free Azure account with a subscription key before using it. See [Try the speech service for free](https://docs.microsoft.com/en-us/azure/cognitive-services/speech-service/overview#try-the-speech-service-for-free).
- Supports text-only training for [custom model](https://docs.microsoft.com/en-us/azure/cognitive-services/speech-service/custom-speech-overview) to achieve higher accuracy for domain-specific terminologies.

This project is not affiliated with Microsoft / Azure in any way.

If there's a better speech recognition service with higher accuracy & reasonable price, please [file an issue](https://github.com/j3soon/speech-to-windows-input/issues).

## How to Use

Prerequisites:
- .Net Framework >= 4.6.1
- [Microsoft Visual C++ Redistributable Package](https://docs.microsoft.com/en-US/cpp/windows/latest-supported-vc-redist?view=msvc-170#visual-studio-2015-2017-2019-and-2022) (required by the [Azure Speech SDK](https://github.com/Azure-Samples/cognitive-services-speech-sdk/tree/master/samples/csharp/dotnetcore/console))
- An Azure subscription key for the Speech service. See [Try the speech service for free](https://docs.microsoft.com/en-us/azure/cognitive-services/speech-service/overview#try-the-speech-service-for-free).
- A Windows PC with a working microphone.

Installation:
- Download the executable in [the release page](https://github.com/j3soon/speech-to-windows-input/releases).
- Copy the Azure key and region text into the config file `config.json`.
- Set up your microphone and make sure you have stable internet connection.
- Launch the program and press `Alt+H` to start speech recognition.
- The program recognizes both English and Chinese by default. Therefore there is a long initial delay for detecting the used language. You should keep only a single language in the `Languages` option in the config file for better user experience.
- See the [Configuration File](#configuration-file) section for more configurations.

## Demo GIFs

With `"Languages": ["en-US"]`:

![](docs/images/demo-en_US.gif)

With `"Languages": ["zh-TW"]`:

![](docs/images/demo-zh_TW.gif)

> The latest version has much faster keyboard input speed than the GIFs above.

Note: If your current input language is set to Chinese, you can select characters after performing speech recognition.

## Features:

(✔️ means implemented, ❌ means not)
- ✔️ [Automatic language detection](https://docs.microsoft.com/en-us/azure/cognitive-services/speech-service/how-to-automatic-language-detection?pivots=programming-language-csharp) (`Languages`)  
  Pre-define a list of languages and let Azure Speech SDK detect the languages used before starting speech recognition. (i.e., Language Detection -> Start Speech Recognition for that specific language). However it causes a long initial delay. (Can be disabled)
- ✔️ [Recognizing custom phrases](https://docs.microsoft.com/en-us/dotnet/api/microsoft.cognitiveservices.speech.phraselistgrammar?view=azure-dotnet) (`PhraseList`)  
  Allows recognition of custom phrases such as names, technical terms, etc.
- ✔️ Input while recognizing (`InputIncrementally`)  
  Allows real-time speech recognition experience. Will modify with backspaces if the recognition result changes.
- ✔️ Automatically add punctuation (`AutoPunctuation`)  
- ✔️ [Continuous recognition](https://docs.microsoft.com/en-us/azure/cognitive-services/speech-service/get-started-speech-to-text?tabs=windowsinstall&pivots=programming-language-csharp#continuous-recognition) (`ContinuousRecognition` & `TotalTimeoutMS`)  
  Instead of performing speech recognition until silence or reaching 15 seconds, let user decide when to stop the recognition by pressing the speech recognition key (`Alt+H`) again. Timeouts in 60 seconds (i.e., 60000 ms) By default.
- ✔️ Use a single key to launch speech recognition (`UseMenuKey`, `UseFxKey`)  
  Most people don't use the [menu/application key](https://en.wikipedia.org/wiki/Menu_key) (`≣ Menu`). So we can override it to behave as the speech recognition key. Similarly, you can override `F1` key to `F24` key.
- ✔️ Send additional trailing characters (`SendTrailingEnter`, `SendTrailingSpace`)
- ✔️ Change comma to whitespace and remove period (`ChineseChatMode`)
- ✔️ Show a semi-transparent GUI overlay when recognizing (`ShowListeningOverlay`)
- ✔️ Hot reload config file
- ✔️ Switch between config files (`UseSwitchConfigKey`)
- ✔️ Forbid multiple program instances
- ✔️ [Arcane options for debugging](https://docs.microsoft.com/en-us/dotnet/api/microsoft.cognitiveservices.speech.detailedspeechrecognitionresult?view=azure-dotnet), [[sample1]](https://github.com/Azure-Samples/cognitive-services-speech-sdk/issues/667), [[sample2]](https://stackoverflow.com/a/51190072) (`PrioritizeLatencyOrAccuracy`, `SoundEffect`, `OutputForm`, `DetailedLog`, `ForceCapitalizeFirstAlphabet`)  
  More debugging features and Azure SDK options that doesn't interest normal users.
- ❌ Mixed language speech recognition  
  It's much better to skip the language detection phase and perform mixed language speech recognition directly to avoid initial delay. Unfortunately Azure SDK doesn't support this yet.
- ❌ Add a GUI menu window to start/stop recognition instead of solely rely on hotkeys.
- ❌ Add a GUI config window to modify the configs instead of direct editing.
- ❌ [Select preferred audio input device](https://docs.microsoft.com/en-us/azure/cognitive-services/speech-service/how-to-select-audio-input-devices)  
  Instead of the default microphone, let the user choose the preferred microphone.
- ❌ Robustness against unstable (or changing) internet connection
- ❌ Robustness against unstable (or changing) microphones
- ❌ Dedicated thread for low-level global key hook

## Configuration File

```json
{
  "AzureSubscriptionKey": "<paste-your-subscription-key>",
  "AzureServiceRegion": "<paste-your-region>",
  "Languages": [
    "en-US",
    "zh-TW"
  ],
  "PhraseList": [],
  "PrioritizeLatencyOrAccuracy": "Latency",
  "SoundEffect": false,
  "InputIncrementally": true,
  "OutputForm": "Text",
  "AutoPunctuation": true,
  "DetailedLog": false,
  "ContinuousRecognition": false,
  "TotalTimeoutMS": 60000,
  "UseMenuKey": false,
  "UseFxKey": 0,
  "SendTrailingEnter": false,
  "SendTrailingSpace": false,
  "ChineseChatMode": false,
  "ForceCapitalizeFirstAlphabet": true,
  "ShowListeningOverlay": true,
  "UseSwitchConfigKey": false
}
```

Make sure to replace `<paste-your-subscription-key>` and `<paste-your-region>` to valid Azure credentials, otherwise, an error will be raised.

- `AzureSubscriptionKey`: Your Azure subscription key.
- `AzureServiceRegion`: Your Azure subscription region. (e.g., `"westus"` or `"eastasia"`)
- `Languages`: A list of languages to perform speech recognition. (No mixed language recognition) See the [supported languages](https://docs.microsoft.com/en-us/azure/cognitive-services/speech-service/language-support#speech-to-text) for more information. If the list only contains a single language, the recognition performance will be much faster since no auto language detection is performed.
- `PhraseList`: A list of of custom phrases such as names, technical terms, etc. For an example, if you are a gamer that types `GG` (i.e., Good Game) a lot, you will want to add `GG` in this list. Otherwise, Azure will recognize it as `JuJu`, `Gigi`, etc.
- `PrioritizeLatencyOrAccuracy`: Select the recognition mode between `"Latency"` and `"Accuracy"`.
- `SoundEffect`: Determines whether the program should play a hint sound when the speech recognition starts/stops.
- `InputIncrementally`: Determines whether the program should input incrementally (revise with backspace along the way), or simply input once when the recognition result is confirmed.
- `OutputForm`: Select the recognition output, can be `"Text"`, `"Lexical"`, or `"Normalized"`. See the [output form details](https://docs.microsoft.com/en-us/dotnet/api/microsoft.cognitiveservices.speech.detailedspeechrecognitionresult?view=azure-dotnet) for more information.
- `AutoPunctuation`: Determines whether to automatically insert punctuation.
- `DetailedLog`: Determines whether to log confidence, recognized words, and other recognition details.
- `ContinuousRecognition`: Determines whether to use continuous recognition, which requires user to manually stop the on-going recognition.
- `TotalTimeoutMS`: When continuous recognition is enabled, the recognition will be stopped after the specified amount of milliseconds, to avoid user forgetting to stop the on-going recognition.
- `UseMenuKey`: Determines whether the [menu/application key](https://en.wikipedia.org/wiki/Menu_key) (`≣ Menu`) can be used as `Alt+H`. (Make the key acts like a dedicated speech recognition key)
- `UseFxKey`: Determines whether the `F1`-`F24` keys can be used as `Alt+H`.
  `"UseFxKey": 0` disables this feature, `"UseFxKey": 1` uses `F1`, and so on.
- `SendTrailingEnter`: Determines whether to send a trailing `enter` after sending inputs.
- `SendTrailingSpace`: Determines whether to send a trailing `space` after sending inputs.
- `ChineseChatMode`: Replaces Chinese comma (`，`) into English whitespace (` `), and removes Chinese period (`。`).
- `ForceCapitalizeFirstAlphabet`: Force capitalization of the first English alphabet in a sentence. This allows better user experience when `InputIncrementally` is enabled.
- `ShowListeningOverlay`: Determines whether to show an indicator microphone overlay window when the program is listening.
- `UseSwitchConfigKey`: Determines whether to enable switching between configs with `Alt+0`, `Alt+1`, and so on.

## Program Hint

```
speech-to-windows-input (STWI) %VERSION_STRING%

Source Code Link (MIT License):

    https://github.com/j3soon/speech-to-windows-input

1. Press Alt+H to convert speech to text input. The recognition stops on (1) microphone silence (2) after 15 seconds (3) Alt+H is pressed again.
2. Press ESC to cancel the on-going speech recognition (no input will be generated).
3. Press Ctrl+C to exit.

Notes:
- The default microphone & internet connection is used for speech recognition.
- If input fails for certain applications, you may need to launch this program with `Run as administrator`.
- The initial recognition delay is for detecting the language used. You can modify the language list to contain only a single language to speed up the process.
```

## Side Notes

Runtime:
- If the program seems stuck while performing speech recognition, it may be caused by unstable internet or microphone connection. Please restart the program.
- If Unicode characters (e.g., Chinese) cannot be shown correctly, you should set the console font to support Unicode.
- If you don't want your credit card be accidentally charged, set the Pricing Tier to `Free F0` instead of `Standard F0` (Pay as You Go). The free tier has [5 hours audio quota each month](https://azure.microsoft.com/en-us/pricing/details/cognitive-services/speech-services/), and will not charge you when exceeding the quota limit.

Compilation:
- Azure Speech service does not support `AnyCPU`, use `x86`/`x64` instead.

## References

Minimal Sample:
- [Quickstart: Recognize speech from a microphone in C# under .NET Framework for Windows](https://github.com/Azure-Samples/cognitive-services-speech-sdk/tree/master/quickstart/csharp/dotnet/from-microphone)
- [Get started with speech-to-text](https://docs.microsoft.com/en-us/azure/cognitive-services/speech-service/get-started-speech-to-text?pivots=programming-language-csharp&tabs=windowsinstall)
