// Licensed under the MIT License. See LICENSE in the project root for license information.

using ElevenLabs.History;
using ElevenLabs.Models;
using ElevenLabs.User;
using ElevenLabs.VoiceGeneration;
using ElevenLabs.Voices;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;
using Utilities.Async;
using Utilities.Audio.Editor;
using Utilities.Extensions.Editor;
using Utilities.WebRequestRest;

namespace ElevenLabs.Editor
{
    public sealed class ElevenLabsEditorWindow : EditorWindow
    {
        [Serializable]
        private class CreateVoicePopupContent : PopupWindowContent
        {
            private class VoiceGenerationArgs : ScriptableObject
            {
                public int tab;

                public int genderSelection;

                public int ageSelection;

                public int accentSelection;

                public float accentStrengthSelection;

                [TextArea]
                public string speechSynthesisTextInput;

                public string voiceName;

                public List<AudioClip> voiceSamples = new List<AudioClip>();

                public readonly Dictionary<string, string> labels = new Dictionary<string, string>();

                public GeneratedVoiceRequest CreateRequest()
                    => new GeneratedVoiceRequest(
                        speechSynthesisTextInput,
                        generatedVoiceOptions.Genders[genderSelection],
                        generatedVoiceOptions.Accents[accentSelection],
                        generatedVoiceOptions.Ages[ageSelection]);
            }

            private readonly Vector2 windowSize = new Vector2(WideColumnWidth * 4, WideColumnWidth * 3);

            private static readonly string[] popupTabTitles = { "Voice Designer", "Voice Cloning" };

            [SerializeField]
            private VoiceGenerationArgs args;

            [SerializeField]
            private string generatedVoiceId;

            [SerializeField]
            private AudioClip generatedVoiceClip;

            private SerializedObject serializedArgs;

            private Vector2 scrollPosition = Vector2.zero;

            public override Vector2 GetWindowSize() => windowSize;

            public override void OnOpen()
            {
                if (args == null)
                {
                    args = CreateInstance<VoiceGenerationArgs>();
                }

                serializedArgs = new SerializedObject(args);
                ResetVoiceDesigner();
            }

            public override void OnGUI(Rect rect)
            {
                serializedArgs.Update();
                EditorGUILayout.Space();
                EditorGUILayout.BeginVertical();
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(TabWidth * 0.5f);
                EditorGUILayout.BeginVertical();
                EditorGUI.BeginChangeCheck();
                args.tab = GUILayout.Toolbar(args.tab, popupTabTitles, expandWidthOption);

                if (EditorGUI.EndChangeCheck())
                {
                    GUI.FocusControl(null);
                    scrollPosition = Vector2.zero;
                }

                GUILayout.Space(TabWidth * 0.5f);

                switch (args.tab)
                {
                    case 0:
                        RenderVoiceDesigner();
                        break;
                    case 1:
                        RenderVoiceCloning();
                        break;
                }

                GUILayout.Space(TabWidth * 0.5f);
                EditorGUILayout.EndVertical();
                GUILayout.Space(TabWidth * 0.5f);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                serializedArgs.ApplyModifiedProperties();
            }

            private void ResetVoiceDesigner()
            {
                args.accentStrengthSelection = 1f;
                args.speechSynthesisTextInput = "First we thought the PC was a calculator. Then we found out how to turn numbers into letters and we thought it was a typewriter.";
            }

            private void RenderVoiceDesigner()
            {
                scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
                var voiceNameProperty = serializedArgs.FindProperty(nameof(args.voiceName));
                EditorGUILayout.PropertyField(voiceNameProperty);
                args.genderSelection = EditorGUILayout.Popup(new GUIContent("Gender"), args.genderSelection, generatedVoiceOptions.Genders.Select(gender => new GUIContent(gender.Name)).ToArray());
                args.ageSelection = EditorGUILayout.Popup(new GUIContent("Age"), args.ageSelection, generatedVoiceOptions.Ages.Select(age => new GUIContent(age.Name)).ToArray());
                args.accentSelection = EditorGUILayout.Popup(new GUIContent("Accent"), args.accentSelection, generatedVoiceOptions.Accents.Select(accent => new GUIContent(accent.Name)).ToArray());
                args.accentStrengthSelection = EditorGUILayout.Slider(new GUIContent("Accent Strength"), args.accentStrengthSelection, (float)generatedVoiceOptions.MinimumAccentStrength, (float)generatedVoiceOptions.MaximumAccentStrength);

                var speechSynthesisTextInputProperty = serializedArgs.FindProperty(nameof(args.speechSynthesisTextInput));
                EditorGUILayout.PropertyField(speechSynthesisTextInputProperty, GUIContent.none, GUILayout.ExpandHeight(true));

                EditorGUILayout.BeginHorizontal();
                { // Text area footer
                    EditorGUILayout.LabelField(new GUIContent($"{args.speechSynthesisTextInput.Length} / {generatedVoiceOptions.MaximumCharacters}"), expandWidthOption);
                    GUILayout.FlexibleSpace();
                    var remainingCharacters = 0;

                    if (userInfo != null)
                    {
                        remainingCharacters = userInfo.CharacterLimit - userInfo.CharacterCount;
                    }

                    EditorGUILayout.LabelField(new GUIContent($"Total quota remaining: {remainingCharacters}"), RightMiddleAlignedLabel, expandWidthOption);
                }
                EditorGUILayout.EndHorizontal();
                GUILayout.Space(TabWidth * 0.5f);

                GUI.enabled = !isCreatingVoice &&
                              !isGeneratingVoiceSample &&
                              args.speechSynthesisTextInput.Length <= generatedVoiceOptions.MaximumCharacters &&
                              args.speechSynthesisTextInput.Length >= generatedVoiceOptions.MinimumCharacters;

                if (GUILayout.Button("Generate", expandWidthOption))
                {
                    EditorApplication.delayCall += () =>
                    {
                        GenerateVoiceSample(args.CreateRequest());
                    };
                }

                GUILayout.Space(TabWidth * 0.5f);
                GUI.enabled = !isCreatingVoice &&
                              !isGeneratingVoiceSample &&
                              !string.IsNullOrWhiteSpace(args.voiceName) &&
                              !string.IsNullOrWhiteSpace(generatedVoiceId) &&
                              generatedVoiceClip != null;

                if (GUILayout.Button("Use Voice", expandWidthOption))
                {
                    EditorApplication.delayCall += CreateGeneratedVoice;
                }

                GUI.enabled = true;
                EditorGUILayout.EndScrollView();
            }

            private static bool isCreatingVoice;

            private async void CreateGeneratedVoice()
            {
                if (isCreatingVoice) { return; }
                isCreatingVoice = true;

                try
                {
                    var request = new CreateVoiceRequest(args.voiceName, generatedVoiceId);
                    await api.VoiceGenerationEndpoint.CreateVoiceAsync(request);
                }
                catch (Exception e)
                {
                    Debug.LogError(e);
                }
                finally
                {
                    isCreatingVoice = false;
                    generatedVoiceId = null;
                    generatedVoiceClip = null;
                    FetchVoices();
                    args = CreateInstance<VoiceGenerationArgs>();
                    editorWindow.Close();
                }
            }

            private static bool isGeneratingVoiceSample;

            private async void GenerateVoiceSample(GeneratedVoiceRequest request)
            {
                if (isGeneratingVoiceSample) { return; }
                isGeneratingVoiceSample = true;

                try
                {
                    var (voiceId, audioClip) = await api.VoiceGenerationEndpoint.GenerateVoiceAsync(request, editorDownloadDirectory);
                    generatedVoiceId = voiceId;
                    generatedVoiceClip = audioClip;
                    AudioEditorUtilities.PlayClipPreview(audioClip);
                }
                catch (Exception e)
                {
                    Debug.LogError(e);
                }
                finally
                {
                    isGeneratingVoiceSample = false;
                    FetchUserInfo();
                    FetchVoices();
                }
            }

            private void RenderVoiceCloning()
            {
                scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

                var voiceNameProperty = serializedArgs.FindProperty(nameof(args.voiceName));
                EditorGUILayout.PropertyField(voiceNameProperty);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"Labels {args.labels.Count} / 5", expandWidthOption);

                GUILayout.FlexibleSpace();

                if (args.labels.Count < 5 &&
                    GUILayout.Button("Add Label", wideColumnWidthOption))
                {
                    EditorApplication.delayCall += () =>
                    {
                        tempLabelKey = string.Empty;
                        args.labels.TryAdd(NewLabel, string.Empty);
                        lastTextControl = $"{LabelControlField}_New Label_{nameof(RenderVoiceCloning)}";
                    };
                }

                EditorGUILayout.EndHorizontal();

                foreach (var label in args.labels)
                {
                    RenderLabel(label, args.labels, editorWindow);
                }

                var voiceSamplesProperty = serializedArgs.FindProperty(nameof(args.voiceSamples));
                EditorGUILayout.PropertyField(voiceSamplesProperty, new GUIContent($"Sample Clips {args.voiceSamples.Count} / 25"));

                GUILayout.Space(TabWidth * 0.5f);
                GUILayout.FlexibleSpace();
                GUI.enabled = !isAddingVoice &&
                              !string.IsNullOrWhiteSpace(args.voiceName) &&
                              args.voiceSamples.Count > 0 &&
                              args.voiceSamples.All(clip => clip != null);

                if (GUILayout.Button("Add Voice", expandWidthOption))
                {
                    EditorApplication.delayCall += AddVoice;
                }

                GUI.enabled = true;
                EditorGUILayout.EndScrollView();
            }

            private static bool isAddingVoice;

            private async void AddVoice()
            {
                if (isAddingVoice) { return; }
                isAddingVoice = true;

                try
                {
                    var samplePaths = from audioClip in args.voiceSamples
                                      where audioClip != null
                                      select Path.GetFullPath(AssetDatabase.GetAssetPath(audioClip));
                    await api.VoicesEndpoint.AddVoiceAsync(args.voiceName, samplePaths, args.labels);
                }
                catch (Exception e)
                {
                    Debug.LogError(e);
                }
                finally
                {
                    isAddingVoice = false;
                    FetchVoices();
                    args = CreateInstance<VoiceGenerationArgs>();
                    editorWindow.Close();
                }
            }
        }

        #region UX Content

        private const int TabWidth = 18;
        private const int EndWidth = 10;
        private const int InnerLabelIndentLevel = 13;
        private const int MaxCharacterLength = 5000;

        private const float InnerLabelWidth = 1.9f;
        private const float DefaultColumnWidth = 96f;
        private const float WideColumnWidth = 128f;
        private const float SettingsLabelWidth = 1.56f;

        private const string NewLabel = "New Label";
        private const string LabelControlField = "LabelControlField";

        private static readonly GUIContent saveDirectoryContent = new GUIContent("Save Directory");

        private static readonly GUIContent guiTitleContent = new GUIContent("Eleven Labs Dashboard");

        private static readonly GUIContent voiceContent = new GUIContent("Voice");

        private static readonly GUIContent modelContent = new GUIContent("Model");

        private static readonly GUIContent stabilityContent = new GUIContent("Stability");

        private static readonly GUIContent moreVariableContent = new GUIContent("More Variable", "Increasing variability can make speech more expressive with output varying between re-generations. It can also lead to instabilities.");

        private static readonly GUIContent moreStableContent = new GUIContent("More Stable", "Increasing stability will make the voice more consistent between re-generations, but it can also make it sounds a bit monotone. On longer text fragments we recommend lowering this value.");

        private static readonly GUIContent clarityContent = new GUIContent("Clarity + Similarity Enhancement");

        private static readonly GUIContent lowClarityContent = new GUIContent("Low", "Low values are recommended if background artifacts are present in generated speech.");

        private static readonly GUIContent highClarityContent = new GUIContent("High", "Recommended. High enhancement boosts overall voice clarity and target speaker similarity. Very high values can cause artifacts, so adjusting this setting to find the optimal value is encouraged.");

        private static readonly GUIContent addNewSampleContent = new GUIContent("Add new Sample(s)");

        private static readonly GUIContent downloadContent = new GUIContent("Download");

        private static readonly GUIContent deleteContent = new GUIContent("Delete");

        private static readonly GUIContent refreshContent = new GUIContent("Refresh");

        private static readonly GUIContent downloadAllHistoryContent = new GUIContent("Download All History");

        private static readonly GUIContent downloadingContent = new GUIContent("Download in progress...");

        private static readonly GUIContent keyContent = new GUIContent("Key");

        private static readonly GUIContent valueContent = new GUIContent("Value");

        private static readonly string[] tabTitles = { "Speech Synthesis", "Voice Lab", "History" };

        private static GUIStyle boldCenteredHeaderStyle;

        private static GUIStyle BoldCenteredHeaderStyle
        {
            get
            {
                if (boldCenteredHeaderStyle == null)
                {
                    var editorStyle = EditorGUIUtility.isProSkin ? EditorStyles.whiteLargeLabel : EditorStyles.largeLabel;

                    if (editorStyle != null)
                    {
                        boldCenteredHeaderStyle = new GUIStyle(editorStyle)
                        {
                            alignment = TextAnchor.MiddleCenter,
                            fontSize = 18,
                            padding = new RectOffset(0, 0, -8, -8)
                        };
                    }
                }

                return boldCenteredHeaderStyle;
            }
        }

        private static GUIStyle rightMiddleAlignedLabel;

        private static GUIStyle RightMiddleAlignedLabel => rightMiddleAlignedLabel ??= new GUIStyle(EditorStyles.label)
        {
            alignment = TextAnchor.MiddleRight,
        };

        private static string DefaultSaveDirectoryKey => $"{Application.productName}_ElevenLabs_EditorDownloadDirectory";

        private static string DefaultSaveDirectory => Application.dataPath;

        private static readonly GUILayoutOption[] defaultColumnWidthOption =
        {
            GUILayout.Width(DefaultColumnWidth)
        };

        private static readonly GUILayoutOption[] wideColumnWidthOption =
        {
            GUILayout.Width(WideColumnWidth)
        };

        private static readonly GUILayoutOption[] expandWidthOption =
        {
            GUILayout.ExpandWidth(true)
        };

        private static readonly GUILayoutOption[] squareWidthOption =
        {
            GUILayout.Width(24)
        };

        #endregion UX Content

        #region Static Content

        private static ElevenLabsClient api;

        private static string editorDownloadDirectory = string.Empty;

        private static bool hasFetchedUserInfo;

        private static bool isFetchingUserInfo;

        private static SubscriptionInfo userInfo;

        private static bool hasFetchedVoices;

        private static IReadOnlyList<Voice> voices = new List<Voice>();

        private static GUIContent[] voiceOptions = Array.Empty<GUIContent>();

        private static Voice currentVoiceOption;

        private static VoiceSettings currentVoiceSettings;

        private static bool hasFetchedModels;

        private static IReadOnlyList<Model> models = new List<Model>();

        private static GUIContent[] modelOptions = Array.Empty<GUIContent>();

        private static Model currentModelOption;

        private static GeneratedVoiceOptions generatedVoiceOptions;

        private static readonly ConcurrentDictionary<string, Dictionary<string, string>> voiceLabels = new ConcurrentDictionary<string, Dictionary<string, string>>();

        private static readonly ConcurrentDictionary<string, IEnumerable<AudioClip>> voiceSampleCache = new ConcurrentDictionary<string, IEnumerable<AudioClip>>();

        private static bool hasFetchedHistory;

        private static bool isFetchingHistory;

        private static IReadOnlyList<HistoryItem> history = new List<HistoryItem>();

        private static readonly ConcurrentDictionary<string, GUIContent> historyItemLabelCache = new ConcurrentDictionary<string, GUIContent>();

        private static readonly ConcurrentDictionary<string, AudioClip> downloadedAudioClips = new ConcurrentDictionary<string, AudioClip>();

        private static GUIContent audioPlayButtonContent;

        private static Rect createVoiceButtonRect;

        private static string lastTextControl;

        private static string tempLabelKey;

        private static string tempLabelValue;

        #endregion Static Content

        [SerializeField]
        private int tab;

        [SerializeField]
        private CreateVoicePopupContent voicePopupContent;

        [SerializeField]
        private string currentVoiceId;

        [SerializeField]
        private string currentModelId;

        [SerializeField]
        private Vector3 voiceSettingsSliderValues = Vector3.zero;

        [SerializeField]
        private bool useSpeakerBoost = true;

        [SerializeField]
        private List<AudioClip> newSampleClips;

        private Vector2 scrollPosition = Vector2.zero;

        private string speechSynthesisTextInput = string.Empty;

        [MenuItem("ElevenLabs/Dashboard")]
        private static void OpenWindow()
        {
            // Dock it next to the Scene View.
            var instance = GetWindow<ElevenLabsEditorWindow>(typeof(SceneView));
            instance.Show();
            instance.titleContent = guiTitleContent;
        }

        private void OnEnable()
        {
            titleContent = guiTitleContent;
            minSize = new Vector2(WideColumnWidth * 5, WideColumnWidth * 4);
            voicePopupContent ??= new CreateVoicePopupContent();
        }

        private void OnFocus()
        {
            audioPlayButtonContent ??= EditorGUIUtility.IconContent("d_PlayButton");

            api ??= new ElevenLabsClient();

            if (!hasFetchedUserInfo ||
                userInfo == null)
            {
                hasFetchedUserInfo = true;
                FetchUserInfo();
            }

            if (!hasFetchedVoices)
            {
                hasFetchedVoices = true;
                FetchVoices();
            }

            if (!hasFetchedModels)
            {
                hasFetchedModels = true;
                FetchModels();
            }

            if (!hasFetchedHistory)
            {
                hasFetchedHistory = true;
                FetchHistory();
            }
            else
            {
                CheckHistory();
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginVertical();
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, expandWidthOption);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(TabWidth);
            EditorGUILayout.BeginVertical();
            { // Begin Header
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Eleven Labs Dashboard", BoldCenteredHeaderStyle);
                EditorGUILayout.Space();

                if (api is not { HasValidAuthentication: true })
                {
                    EditorGUILayout.HelpBox($"No valid {nameof(ElevenLabsConfiguration)} was found. This tool requires that you set your API key.", MessageType.Error);
                    return;
                }

                EditorGUILayout.Space();
                EditorGUI.BeginChangeCheck();
                tab = GUILayout.Toolbar(tab, tabTitles, expandWidthOption);

                if (EditorGUI.EndChangeCheck())
                {
                    GUI.FocusControl(null);
                }

                EditorGUILayout.LabelField(saveDirectoryContent);

                if (string.IsNullOrWhiteSpace(editorDownloadDirectory))
                {
                    editorDownloadDirectory = EditorPrefs.GetString(DefaultSaveDirectoryKey, DefaultSaveDirectory);
                }

                EditorGUILayout.BeginHorizontal();
                {
                    EditorGUILayout.TextField(editorDownloadDirectory, expandWidthOption);

                    if (GUILayout.Button("Reset", wideColumnWidthOption))
                    {
                        editorDownloadDirectory = DefaultSaveDirectory;
                        EditorPrefs.SetString(DefaultSaveDirectoryKey, editorDownloadDirectory);
                    }
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
                {
                    if (GUILayout.Button("Change Save Directory", expandWidthOption))
                    {
                        EditorApplication.delayCall += () =>
                        {
                            var result = EditorUtility.OpenFolderPanel("Save Directory", editorDownloadDirectory, string.Empty);

                            if (!string.IsNullOrWhiteSpace(result))
                            {
                                editorDownloadDirectory = result;
                                EditorPrefs.SetString(DefaultSaveDirectoryKey, editorDownloadDirectory);
                            }
                        };
                    }
                }
                EditorGUILayout.EndHorizontal();
            } // End Header
            EditorGUILayout.EndVertical();
            GUILayout.Space(EndWidth);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();
            EditorGUI.indentLevel++;

            switch (tab)
            {
                case 0:
                    RenderSpeechSynthesis();
                    break;
                case 1:
                    RenderVoiceLab();
                    break;
                case 2:
                    RenderHistory();
                    break;
            }

            EditorGUI.indentLevel--;
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private static async void FetchUserInfo()
        {
            if (isFetchingUserInfo) { return; }
            isFetchingUserInfo = true;

            try
            {
                userInfo = await api.UserEndpoint.GetSubscriptionInfoAsync();
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
            finally
            {
                isFetchingUserInfo = false;
            }
        }

        private static bool isFetchingVoices;

        private static async void FetchVoices()
        {
            if (isFetchingVoices) { return; }
            isFetchingVoices = true;

            try
            {
                voices = await api.VoicesEndpoint.GetAllVoicesAsync();
                voiceOptions = voices.OrderBy(voice => voice.Name).Select(voice => new GUIContent($"{voice.Category}/{voice.Name}")).ToArray();
                generatedVoiceOptions = await api.VoiceGenerationEndpoint.GetVoiceGenerationOptionsAsync();
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
            finally
            {
                isFetchingVoices = false;
                voiceLabels.Clear();
                voiceSampleCache.Clear();
            }
        }

        private static bool isFetchingModels;

        private async void FetchModels()
        {
            if (isFetchingModels) { return; }
            isFetchingModels = true;

            try
            {
                models = await api.ModelsEndpoint.GetModelsAsync();
                modelOptions = models.OrderBy(model => model.Name).Select(model => new GUIContent(model.Name, model.Description)).ToArray();
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
            finally
            {
                isFetchingModels = false;
            }
        }

        private static async void FetchHistory()
        {
            if (isFetchingHistory) { return; }
            isFetchingHistory = true;

            try
            {
                history = await api.HistoryEndpoint.GetHistoryAsync().ConfigureAwait(true);
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
            finally
            {
                historyItemLabelCache.Clear();
                CheckHistory();
                isFetchingHistory = false;
            }
        }

        private static async void CheckHistory()
        {
            if (history == null) { return; }
            var assets = AssetDatabase.FindAssets($"t:{nameof(AudioClip)}");
            downloadedAudioClips.Clear();

            if (Directory.Exists(Application.streamingAssetsPath))
            {
                var clipPaths = Directory.GetFiles(Application.streamingAssetsPath, "*.mp3", SearchOption.AllDirectories);

                foreach (var clipPath in clipPaths)
                {
                    var key = history.FirstOrDefault(item => clipPath.Contains(item.Id) || clipPath.Contains(item.TextHash));

                    if (key != null)
                    {
                        var audioClip = await Rest.DownloadAudioClipAsync($"file://{clipPath}", AudioType.MPEG, Path.GetFileNameWithoutExtension(clipPath), parameters: null);
                        downloadedAudioClips.TryAdd(key.Id, audioClip);
                    }
                }
            }

            foreach (var guid in assets)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                var assetName = Path.GetFileNameWithoutExtension(assetPath);

                var key = history.FirstOrDefault(item => assetName.Equals(item.Id) || assetName.Equals(item.TextHash));

                if (key != null)
                {
                    var audioClip = AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);
                    downloadedAudioClips.TryAdd(key.Id, audioClip);
                }
            }
        }

        #region Voice Synthesis

        private void RenderSpeechSynthesis()
        {
            EditorGUILayout.BeginHorizontal();
            { // Header
                EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();

                GUI.enabled = !isFetchingVoices || !isFetchingUserInfo;

                if (GUILayout.Button(refreshContent, defaultColumnWidthOption))
                {
                    EditorApplication.delayCall += () =>
                    {
                        FetchVoices();
                        FetchModels();
                        FetchUserInfo();
                    };
                }

                EditorGUILayout.Space(EndWidth);
                GUI.enabled = true;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            EditorGUILayout.Space();
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical();
            { // Body
                var prevLabelWidth = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = WideColumnWidth * SettingsLabelWidth;

                EditorGUILayout.BeginHorizontal();
                { // voice dropdown
                    var voiceIndex = -1;

                    currentVoiceOption ??= !string.IsNullOrWhiteSpace(currentVoiceId)
                        ? voices?.FirstOrDefault(voice => voice.Id == currentVoiceId)
                        : voices?.FirstOrDefault();

                    if (currentVoiceOption != null)
                    {
                        for (var i = 0; i < voiceOptions.Length; i++)
                        {
                            if (voiceOptions[i].text.Contains(currentVoiceOption.Name))
                            {
                                voiceIndex = i;
                                break;
                            }
                        }

                        if (currentVoiceSettings == null ||
                            voiceSettingsSliderValues == Vector3.zero)
                        {
                            GetDefaultVoiceSettings(currentVoiceOption);
                        }
                    }

                    EditorGUI.BeginChangeCheck();
                    voiceIndex = EditorGUILayout.Popup(voiceContent, voiceIndex, voiceOptions);

                    if (EditorGUI.EndChangeCheck())
                    {
                        currentVoiceOption = voices?.FirstOrDefault(voice => voiceOptions[voiceIndex].text.Contains($"{voice.Category}/{voice.Name}"));
                        currentVoiceId = currentVoiceOption!.Id;
                        EditorApplication.delayCall += () => GetDefaultVoiceSettings(currentVoiceOption);
                    }
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space();

                GUI.enabled = currentVoiceSettings != null && !isGettingDefaultVoiceSettings;
                { // Stability and Similarity
                    EditorGUI.BeginChangeCheck();

                    voiceSettingsSliderValues.x = EditorGUILayout.Slider(stabilityContent, voiceSettingsSliderValues.x, 0f, 1f);
                    EditorGUILayout.BeginHorizontal();
                    {
                        StartIndent(InnerLabelIndentLevel);
                        EditorGUIUtility.labelWidth = WideColumnWidth * InnerLabelWidth;
                        EditorGUILayout.LabelField(moreVariableContent, expandWidthOption);
                        GUILayout.FlexibleSpace();
                        EditorGUILayout.LabelField(moreStableContent, RightMiddleAlignedLabel, expandWidthOption);
                        EditorGUIUtility.labelWidth = WideColumnWidth * SettingsLabelWidth;
                        EndIndent(InnerLabelIndentLevel);
                    }
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.Space(EndWidth);
                    voiceSettingsSliderValues.y = EditorGUILayout.Slider(clarityContent, voiceSettingsSliderValues.y, 0f, 1f);
                    EditorGUILayout.BeginHorizontal();
                    {
                        StartIndent(InnerLabelIndentLevel);
                        EditorGUIUtility.labelWidth = WideColumnWidth * InnerLabelWidth;
                        EditorGUILayout.LabelField(lowClarityContent, expandWidthOption);
                        GUILayout.FlexibleSpace();
                        EditorGUILayout.LabelField(highClarityContent, RightMiddleAlignedLabel, expandWidthOption);
                        EditorGUIUtility.labelWidth = WideColumnWidth * SettingsLabelWidth;
                        EndIndent(InnerLabelIndentLevel);
                    }
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.Space(EndWidth);
                    voiceSettingsSliderValues.z = EditorGUILayout.Slider("Style", voiceSettingsSliderValues.z, 0f, 1f);
                    EditorGUILayout.BeginHorizontal();
                    {
                        StartIndent(InnerLabelIndentLevel);
                        EditorGUIUtility.labelWidth = WideColumnWidth * InnerLabelWidth;
                        EditorGUILayout.LabelField("Low", expandWidthOption);
                        GUILayout.FlexibleSpace();
                        EditorGUILayout.LabelField("High", RightMiddleAlignedLabel, expandWidthOption);
                        EditorGUIUtility.labelWidth = WideColumnWidth * SettingsLabelWidth;
                        EndIndent(InnerLabelIndentLevel);
                    }
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.Space(EndWidth);
                    useSpeakerBoost = EditorGUILayout.Toggle("Speaker Boost", useSpeakerBoost);
                    EditorGUILayout.Space();
                }

                GUI.enabled = !isFetchingModels;
                EditorGUILayout.BeginHorizontal();
                { // model dropdown
                    var modelIndex = -1;

                    currentModelOption ??= !string.IsNullOrWhiteSpace(currentModelId)
                        ? models?.FirstOrDefault(model => model.Id == currentModelId)
                        : models?.FirstOrDefault();

                    if (currentModelOption != null)
                    {
                        for (var i = 0; i < modelOptions.Length; i++)
                        {
                            if (modelOptions[i].text.Contains(currentModelOption.Name))
                            {
                                modelIndex = i;
                                break;
                            }
                        }
                    }

                    EditorGUI.BeginChangeCheck();
                    modelIndex = EditorGUILayout.Popup(modelContent, modelIndex, modelOptions);

                    if (EditorGUI.EndChangeCheck())
                    {
                        currentModelOption = models?.FirstOrDefault(model => modelOptions[modelIndex].text.Contains($"{model.Name}"));
                        currentModelId = currentModelOption!.Id;
                    }
                }
                EditorGUILayout.EndHorizontal();
                GUILayout.Space(EndWidth);

                EditorGUILayout.BeginVertical();
                EditorGUILayout.BeginHorizontal();
                { // Text Area Header
                    EditorGUILayout.LabelField("Text", EditorStyles.boldLabel, expandWidthOption);
                    GUILayout.FlexibleSpace();

                    if (GUILayout.Button("Reset to Default", wideColumnWidthOption))
                    {
                        EditorApplication.delayCall += () => GetDefaultVoiceSettings(currentVoiceOption);
                    }
                }

                if (EditorGUI.EndChangeCheck())
                {
                    currentVoiceSettings = new VoiceSettings(voiceSettingsSliderValues.x, voiceSettingsSliderValues.y, useSpeakerBoost, voiceSettingsSliderValues.z);
                }

                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();

                GUI.enabled = true;
                EditorGUIUtility.labelWidth = prevLabelWidth;
                GUILayout.Space(EndWidth);

                EditorGUI.BeginChangeCheck();
                EditorStyles.textField.wordWrap = true;
                speechSynthesisTextInput = EditorGUILayout.TextArea(speechSynthesisTextInput, GUILayout.ExpandHeight(true));

                if (EditorGUI.EndChangeCheck())
                {
                    if (speechSynthesisTextInput.Length > MaxCharacterLength)
                    {
                        speechSynthesisTextInput = speechSynthesisTextInput[..MaxCharacterLength];
                    }
                }

                GUILayout.Space(EndWidth);
                EditorGUILayout.BeginHorizontal();
                { // Text area footer
                    EditorGUILayout.LabelField(new GUIContent($"{speechSynthesisTextInput.Length} / {MaxCharacterLength}"), expandWidthOption);
                    GUILayout.FlexibleSpace();
                    var remainingCharacters = 0;

                    if (userInfo != null)
                    {
                        remainingCharacters = userInfo.CharacterLimit - userInfo.CharacterCount;
                    }

                    EditorGUILayout.LabelField(new GUIContent($"Total quota remaining: {remainingCharacters}"), RightMiddleAlignedLabel, expandWidthOption);
                }
                EditorGUILayout.EndHorizontal();
                GUILayout.Space(EndWidth);

                EditorGUILayout.BeginHorizontal();
                {
                    GUI.enabled = !isSynthesisRunning && speechSynthesisTextInput.Length is > 0 and < MaxCharacterLength;
                    GUILayout.Space(TabWidth);

                    if (GUILayout.Button("Generate", expandWidthOption))
                    {
                        EditorApplication.delayCall += GenerateSynthesizedText;
                    }
                    GUI.enabled = true;
                }
                EditorGUILayout.EndHorizontal();
            }
            GUILayout.Space(EndWidth);
            GUILayout.EndVertical();
            GUILayout.Space(EndWidth);
            GUILayout.EndHorizontal();
        }

        private static bool isSynthesisRunning;

        private async void GenerateSynthesizedText()
        {
            if (isSynthesisRunning) { return; }
            isSynthesisRunning = true;

            try
            {
                if (string.IsNullOrWhiteSpace(speechSynthesisTextInput))
                {
                    throw new ArgumentNullException(nameof(speechSynthesisTextInput));
                }

                if (currentVoiceOption == null)
                {
                    throw new ArgumentNullException(nameof(currentVoiceOption));
                }

                if (currentVoiceSettings == null)
                {
                    throw new ArgumentNullException(nameof(currentVoiceSettings));
                }

                if (currentModelOption == null)
                {
                    throw new ArgumentNullException(nameof(currentModelOption));
                }

                var (clipPath, audioClip) = await api.TextToSpeechEndpoint.TextToSpeechAsync(
                    speechSynthesisTextInput,
                    currentVoiceOption,
                    currentVoiceSettings,
                    currentModelOption,
                    saveDirectory: editorDownloadDirectory);

                await Awaiters.UnityMainThread;

                if (clipPath.Contains(Application.dataPath))
                {
                    var importPath = clipPath.Replace(Application.dataPath, "Assets").Replace("\\", "/");
                    AssetDatabase.ImportAsset(importPath);
                    AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                    EditorGUIUtility.PingObject(audioClip);
                    Selection.activeObject = audioClip;
                }

                AudioEditorUtilities.PlayClipPreview(audioClip);

                FetchUserInfo();
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
            finally
            {
                isSynthesisRunning = false;
            }
        }

        private static bool isGettingDefaultVoiceSettings;

        private async void GetDefaultVoiceSettings(Voice voice)
        {
            if (isGettingDefaultVoiceSettings) { return; }
            isGettingDefaultVoiceSettings = true;

            try
            {
                var defaultVoiceSettings = await api.VoicesEndpoint.GetDefaultVoiceSettingsAsync();
                await api.VoicesEndpoint.EditVoiceSettingsAsync(voice, defaultVoiceSettings);
                currentVoiceSettings = await api.VoicesEndpoint.GetVoiceSettingsAsync(voice);
                voiceSettingsSliderValues = new Vector3(defaultVoiceSettings.Stability, defaultVoiceSettings.SimilarityBoost, 0.45f);
                useSpeakerBoost = true;
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
            finally
            {
                isGettingDefaultVoiceSettings = false;
            }
        }

        #endregion Voice Synthesis

        #region Voice Lab

        private void RenderVoiceLab()
        {
            string voiceCount;

            if (!isFetchingVoices && userInfo != null)
            {
                var generatedCount = voices.Count(voice => voice.Category is "generated" or "cloned");
                voiceCount = $"{generatedCount}/{userInfo.VoiceLimit}";
            }
            else
            {
                voiceCount = "~/~";
            }

            EditorGUILayout.BeginHorizontal();
            { // Header
                EditorGUILayout.LabelField($"Voices {voiceCount}", EditorStyles.boldLabel, defaultColumnWidthOption);

                GUI.enabled = !isFetchingVoices;

                if (GUILayout.Button("Create New Voice", expandWidthOption))
                {
                    PopupWindow.Show(createVoiceButtonRect, voicePopupContent);
                }

                if (Event.current.type == EventType.Repaint)
                {
                    createVoiceButtonRect = GUILayoutUtility.GetLastRect();
                }

                GUILayout.FlexibleSpace();

                if (GUILayout.Button(refreshContent, defaultColumnWidthOption))
                {
                    EditorApplication.delayCall += FetchVoices;
                }

                EditorGUILayout.Space(EndWidth);
                GUI.enabled = true;
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();
            EditorGUILayout.Space();

            foreach (var voice in voices)
            {
                // ReSharper disable once IdentifierTypo
                // ReSharper disable once StringLiteralTypo
                const string premade = "premade";
                const string cloned = "cloned";

                if (voice.Category.Contains(premade)) { continue; }
                EditorGUILayoutExtensions.Divider();

                var isCloned = voice.Category.Contains(cloned);

                EditorGUILayout.BeginVertical();
                EditorGUILayout.BeginHorizontal();
                {
                    EditorGUILayout.LabelField($"{voice.Category}/{voice.Name} | {voice.Id}", EditorStyles.boldLabel, GUILayout.MinWidth(WideColumnWidth * 3), GUILayout.ExpandWidth(true));
                    GUILayout.FlexibleSpace();

                    GUI.enabled = !isFetchingVoices;

                    if (GUILayout.Button(deleteContent, defaultColumnWidthOption))
                    {
                        EditorApplication.delayCall += () => DeleteVoice(voice);
                    }

                    GUI.enabled = true;
                }
                EditorGUILayout.Space(EndWidth);
                EditorGUILayout.EndHorizontal();
                EditorGUI.indentLevel++;

                if (!voiceLabels.TryGetValue(voice.Id, out var cachedLabels))
                {
                    cachedLabels = new Dictionary<string, string>();

                    foreach (var (key, value) in voice.Labels)
                    {
                        cachedLabels.TryAdd(key, value);
                    }

                    voiceLabels.TryAdd(voice.Id, cachedLabels);
                }

                EditorGUILayout.LabelField($"Labels {cachedLabels?.Count ?? 0} / 5");
                EditorGUILayout.Space();
                EditorGUI.indentLevel++;

                if (cachedLabels is { Count: > 0 })
                {
                    var hasNewLabel = cachedLabels.ContainsKey(NewLabel);

                    foreach (var cachedLabel in cachedLabels)
                    {
                        if (hasNewLabel &&
                            cachedLabel.Key.Contains(NewLabel))
                        {
                            continue;
                        }

                        RenderLabel(cachedLabel, cachedLabels, this);
                    }

                    if (hasNewLabel)
                    {
                        RenderLabel(cachedLabels.FirstOrDefault(pair => pair.Key.Contains(NewLabel)), cachedLabels, this);
                    }
                }

                EditorGUILayout.Space();
                GUI.enabled = !isFetchingVoices;

                EditorGUILayout.BeginHorizontal();
                {
                    EditorGUILayout.Space(TabWidth * 2);

                    if (cachedLabels is { Count: < 5 } &&
                        !cachedLabels.ContainsKey(NewLabel) &&
                        GUILayout.Button("Add New Label", wideColumnWidthOption))
                    {
                        tempLabelKey = string.Empty;
                        cachedLabels.TryAdd(NewLabel, string.Empty);
                        lastTextControl = $"{LabelControlField}_New Label_{nameof(RenderVoiceLab)}";
                    }

                    GUILayout.FlexibleSpace();

                    bool IsLabelsDirty()
                    {
                        var isDirty = false;

                        foreach (var (key, cachedValue) in cachedLabels)
                        {
                            if (voice.Labels.TryGetValue(key, out var realValue))
                            {
                                isDirty |= cachedValue != realValue;
                            }
                            else
                            {
                                isDirty = true;
                            }
                        }

                        foreach (var (key, realValue) in voice.Labels)
                        {
                            if (cachedLabels.TryGetValue(key, out var cachedValue))
                            {
                                isDirty |= cachedValue != realValue;
                            }
                            else
                            {
                                isDirty = true;
                            }
                        }

                        return isDirty;
                    }

                    GUI.enabled = !isFetchingVoices && IsLabelsDirty();

                    if (GUILayout.Button("Update Labels", defaultColumnWidthOption))
                    {
                        EditorApplication.delayCall += () =>
                        {
                            EditVoice(voice, null, cachedLabels);
                        };
                    }

                    GUILayout.Space(EndWidth);
                }
                EditorGUILayout.EndHorizontal();
                GUI.enabled = true;
                EditorGUI.indentLevel--;

                if (isCloned)
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField($"Samples {voice.Samples?.Count ?? 0} / 25");
                    EditorGUILayout.Space();
                    EditorGUI.indentLevel++;

                    if ((voice.Samples?.Count ?? 0) < 25)
                    {
                        EditorGUILayout.BeginHorizontal();
                        {
                            var thisSo = new SerializedObject(this);
                            var sampleClipsProperty = thisSo.FindProperty(nameof(newSampleClips));
                            EditorGUILayout.PropertyField(sampleClipsProperty, addNewSampleContent, true);
                            thisSo.ApplyModifiedProperties();

                            GUI.enabled = (newSampleClips?.Count > 0 && newSampleClips?.Count <= 25 - voice.Samples.Count) && !isFetchingVoices;

                            if (GUILayout.Button(addNewSampleContent, wideColumnWidthOption))
                            {
                                EditorApplication.delayCall += () =>
                                {
                                    EditVoice(voice, newSampleClips);
                                    newSampleClips = new List<AudioClip>();
                                };
                            }

                            GUI.enabled = true;
                        }
                        GUILayout.Space(EndWidth);
                        EditorGUILayout.EndHorizontal();
                    }

                    EditorGUI.indentLevel--;
                }

                if (voice.Samples != null)
                {
                    EditorGUI.indentLevel++;

                    foreach (var voiceSample in voice.Samples)
                    {
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField($"{voiceSample.Id} | {voiceSample.FileName}", expandWidthOption);

                        GUI.enabled = !isFetchingVoices;

                        if (GUILayout.Button(deleteContent, defaultColumnWidthOption))
                        {
                            EditorApplication.delayCall += () => DeleteVoiceSample(voice, voiceSample);
                        }

                        GUILayout.Space(EndWidth);
                        EditorGUILayout.EndHorizontal();
                        EditorGUILayout.BeginHorizontal();
                        var fileName = Path.GetFileNameWithoutExtension(voiceSample.FileName);
                        var samples = GetSampleClips($"t:{nameof(AudioClip)} {fileName}").ToList();
                        samples.AddRange(GetSampleClips($"t:{nameof(AudioClip)} {voiceSample.Id}"));

                        switch (samples)
                        {
                            case { Count: 0 }:
                                GUI.enabled = !isFetchingVoices;
                                GUILayout.Space(DefaultColumnWidth * 0.5f);

                                if (GUILayout.Button(downloadContent, defaultColumnWidthOption))
                                {
                                    EditorApplication.delayCall += () => DownloadVoiceSample(voice, voiceSample);
                                }

                                GUILayout.FlexibleSpace();
                                break;
                            case { Count: 1 }:
                                EditorGUILayout.BeginHorizontal();
                                GUILayout.Space(TabWidth * EditorGUI.indentLevel);
                                var clip = samples[0];
                                GUI.enabled = true;

                                if (GUILayout.Button(audioPlayButtonContent, squareWidthOption))
                                {
                                    AudioEditorUtilities.PlayClipPreview(clip);
                                }

                                var @object = EditorGUILayout.ObjectField(GUIContent.none, clip, typeof(AudioClip), false, expandWidthOption);
                                EditorGUILayout.EndHorizontal();

                                if (@object == null)
                                {
                                    EditorApplication.delayCall += FetchVoices;
                                }
                                break;
                            default:
                                EditorGUILayout.LabelField($"Found multiple matches for {fileName}", expandWidthOption);
                                break;
                        }

                        GUI.enabled = true;
                        GUILayout.Space(EndWidth);
                        EditorGUILayout.EndHorizontal();
                    }

                    EditorGUI.indentLevel--;
                }

                EditorGUI.indentLevel--;
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space();
            }
        }

        private static void RenderLabel(KeyValuePair<string, string> label, Dictionary<string, string> cachedLabels, EditorWindow editorWindow, [CallerMemberName] string callerMemberName = "")
        {
            EditorGUILayout.BeginHorizontal();
            {
                var (key, value) = label;
                var prevLabelWidth = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = DefaultColumnWidth;
                EditorGUI.BeginChangeCheck();
                var keyTextControl = $"{LabelControlField}_{key}_{callerMemberName}";
                GUI.SetNextControlName(keyTextControl);
                var newKey = EditorGUILayout.TextField(keyContent, key);

                if (lastTextControl == keyTextControl)
                {
                    GUI.FocusControl(keyTextControl);
                    EditorGUI.FocusTextInControl(keyTextControl);
                    lastTextControl = string.Empty;
                }

                if (EditorGUI.EndChangeCheck())
                {
                    tempLabelKey = newKey;
                }

                if (Event.current.isKey && Event.current.keyCode == KeyCode.Tab &&
                    GUI.GetNameOfFocusedControl() == keyTextControl &&
                    !string.IsNullOrWhiteSpace(tempLabelKey))
                {
                    if (key != tempLabelKey)
                    {
                        lastTextControl = $"{LabelControlField}_{tempLabelKey}_{callerMemberName}";

                        EditorApplication.delayCall += () =>
                        {
                            if (cachedLabels.TryGetValue(key, out _))
                            {
                                cachedLabels.Remove(key, out _);
                            }

                            if (!cachedLabels.TryAdd(tempLabelKey, string.Empty))
                            {
                                Debug.LogError($"failed to add label {tempLabelKey}");
                            }

                            tempLabelKey = string.Empty;
                            lastTextControl = $"{lastTextControl}_value_{callerMemberName}";
                            editorWindow.Repaint();
                        };
                    }
                }

                EditorGUI.BeginChangeCheck();
                var valueTextControl = $"{LabelControlField}_{key}_value_{callerMemberName}";
                GUI.SetNextControlName(valueTextControl);
                var newValue = EditorGUILayout.TextField(valueContent, value);

                if (lastTextControl == valueTextControl)
                {
                    GUI.FocusControl(valueTextControl);
                    EditorGUI.FocusTextInControl(valueTextControl);
                    lastTextControl = string.Empty;
                }

                EditorGUIUtility.labelWidth = prevLabelWidth;

                if (EditorGUI.EndChangeCheck())
                {
                    tempLabelValue = newValue;

                    EditorApplication.delayCall += () =>
                    {
                        if (!string.IsNullOrWhiteSpace(tempLabelValue))
                        {
                            cachedLabels[key] = tempLabelValue;
                        }

                        tempLabelValue = string.Empty;
                        editorWindow.Repaint();
                    };
                }

                GUI.enabled = !isFetchingVoices;

                if (GUILayout.Button(deleteContent, defaultColumnWidthOption))
                {
                    EditorApplication.delayCall += () =>
                    {
                        if (!EditorUtility.DisplayDialog(
                                "Alert!",
                                $"Are you sure you want to delete {key}:{value} ?", "Yes",
                                "No"))
                        {
                            return;
                        }

                        cachedLabels.Remove(key, out _);
                    };
                }

                GUI.enabled = true;
            }

            if (editorWindow is ElevenLabsEditorWindow)
            {
                GUILayout.Space(EndWidth);
            }

            EditorGUILayout.EndHorizontal();
        }

        private static async void DeleteVoice(Voice voice)
        {
            if (!EditorUtility.DisplayDialog(
                    "Alert!",
                    $"Are you sure you want to delete voice {voice.Id} \"{voice.Name}\"?", "Yes",
                    "No"))
            {
                return;
            }

            try
            {
                var result = await api.VoicesEndpoint.DeleteVoiceAsync(voice);

                if (!result)
                {
                    Debug.LogError($"Failed to delete voice: {voice.Name}!");
                }
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
            finally
            {
                FetchVoices();
            }
        }

        private static async void EditVoice(Voice voice, List<AudioClip> audioClips = null, IReadOnlyDictionary<string, string> labels = null)
        {
            try
            {
                var audioClipPaths = new List<string>();

                if (audioClips != null)
                {
                    EditorUtility.DisplayProgressBar("Uploading voice sample...", $"Uploading {audioClips.Count} voice samples: {string.Join(", ", audioClips.Select(clip => clip.name))}", -1);

                    foreach (var audioClip in audioClips)
                    {
                        var audioClipPath = Path.GetFullPath(AssetDatabase.GetAssetPath(audioClip));

                        if (string.IsNullOrWhiteSpace(audioClipPath))
                        {
                            throw new ArgumentNullException(nameof(audioClipPath), $"AssetDatabase failed to locate {audioClip.name}!");
                        }

                        if (!File.Exists(audioClipPath))
                        {
                            throw new ArgumentNullException(nameof(audioClipPath), $"Failed to find valid path to {audioClip.name}: \"{audioClipPath}\"");
                        }

                        audioClipPaths.Add(audioClipPath);
                    }
                }
                else
                {
                    EditorUtility.DisplayProgressBar("Updating voice labels...", $"Updating voice labels for {voice.Id}", -1);

                    if (labels == null)
                    {
                        throw new ArgumentNullException(nameof(labels));
                    }
                }

                var result = await api.VoicesEndpoint.EditVoiceAsync(voice, audioClipPaths, labels);

                if (!result)
                {
                    Debug.LogWarning("Failed to update voice!");
                }
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                FetchVoices();
            }
        }

        private static async void DeleteVoiceSample(Voice voice, Sample voiceSample)
        {
            if (!EditorUtility.DisplayDialog(
                    "Alert!",
                    $"Are you sure you want to delete sample {voiceSample.Id} from {voice.Name}?", "Yes",
                    "No"))
            {
                return;
            }

            try
            {
                EditorUtility.DisplayProgressBar("Deleting voice sample...", $"Deleting {voiceSample.Id}", -1);
                await api.VoicesEndpoint.DeleteVoiceSampleAsync(voice, voiceSample);
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                FetchVoices();
            }
        }

        private static async void DownloadVoiceSample(Voice sample, Sample voiceSample)
        {
            try
            {
                await api.VoicesEndpoint.GetVoiceSampleAsync(sample, voiceSample, editorDownloadDirectory);
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
            finally
            {
                voiceSampleCache.Clear();
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            }
        }

        #endregion Voice Lab

        #region History

        private static void RenderHistory()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.BeginVertical();

            EditorGUILayout.BeginHorizontal();
            { //Header
                EditorGUILayout.LabelField("History", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();

                GUI.enabled = !isFetchingVoices || !isFetchingUserInfo;

                if (GUILayout.Button(refreshContent, defaultColumnWidthOption))
                {
                    EditorApplication.delayCall += FetchHistory;
                }

                GUI.enabled = true;
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();

            EditorGUILayout.BeginHorizontal();
            {
                GUILayout.Space(TabWidth);

                GUI.enabled = !isFetchingHistory && history.Count != downloadedAudioClips.Count && !isDownloadingHistoryItem;

                if (GUILayout.Button(isDownloadingHistoryItem ? downloadingContent : downloadAllHistoryContent, expandWidthOption))
                {
                    EditorApplication.delayCall += () => DownloadHistoryAudio(history);
                }

                GUI.enabled = true;
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();

            for (var i = 0; i < history.Count; i++)
            {
                var historyItem = history[i];
                EditorGUI.indentLevel++;

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(GetHistoryItemLabel(historyItem));

                if (!downloadedAudioClips.TryGetValue(historyItem.Id, out var audioClip))
                {
                    downloadedAudioClips.TryGetValue(historyItem.TextHash, out audioClip);
                }

                var isDownloaded = audioClip != null;
                GUI.enabled = !isFetchingHistory && !isDownloadingHistoryItem;

                if (!isDownloaded &&
                    GUILayout.Button(downloadContent, defaultColumnWidthOption))
                {
                    EditorApplication.delayCall += () => DownloadHistoryAudio(new[] { historyItem });
                }

                if (GUILayout.Button(deleteContent, defaultColumnWidthOption))
                {
                    EditorApplication.delayCall += () => DeleteHistoryItem(historyItem);
                }

                GUI.enabled = true;

                EditorGUILayout.EndHorizontal();

                GUI.enabled = false;

                if (isDownloaded)
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(TabWidth * EditorGUI.indentLevel);

                    GUI.enabled = true;
                    if (GUILayout.Button(audioPlayButtonContent, squareWidthOption))
                    {
                        AudioEditorUtilities.PlayClipPreview(audioClip);
                    }

                    GUI.enabled = false;
                    EditorGUILayout.ObjectField(GUIContent.none, audioClip, typeof(AudioClip), false, expandWidthOption);
                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.TextArea(historyItem.Text);
                GUI.enabled = true;
                EditorGUI.indentLevel--;
                EditorGUILayout.Space();
            }

            EditorGUILayout.EndVertical();
            GUILayout.Space(EndWidth);
            EditorGUILayout.EndHorizontal();
        }

        private static bool isDownloadingHistoryItem;

        private static async void DownloadHistoryAudio(IEnumerable<HistoryItem> items)
        {
            if (isDownloadingHistoryItem) { return; }
            isDownloadingHistoryItem = true;

            var historyItemsToDownload = items.Select(item => item.Id).ToList();

            foreach (var (key, clipPath) in downloadedAudioClips)
            {
                var clipName = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(clipPath));

                if (historyItemsToDownload.Contains(clipName))
                {
                    historyItemsToDownload.Remove(clipName);
                }
            }

            if (historyItemsToDownload.Count > 0)
            {
                EditorUtility.DisplayProgressBar("Downloading history...", $"Downloading {historyItemsToDownload.Count} items...", -1);
                var count = 0;
                var progressReport = new Progress<string>(message =>
                {
                    EditorUtility.DisplayProgressBar("Downloading history...", message, ++count / (float)historyItemsToDownload.Count);
                });

                EditorApplication.LockReloadAssemblies();

                try
                {
                    await api.HistoryEndpoint.DownloadHistoryItemsAsync(historyItemsToDownload, editorDownloadDirectory, progressReport);
                }
                catch (Exception e)
                {
                    Debug.LogError(e);
                }
                finally
                {
                    await Awaiters.UnityMainThread;
                    EditorApplication.UnlockReloadAssemblies();
                    EditorUtility.ClearProgressBar();
                    FetchHistory();
                    AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                }
            }

            isDownloadingHistoryItem = false;
            EditorUtility.ClearProgressBar();
        }

        private static async void DeleteHistoryItem(HistoryItem item)
        {
            if (!EditorUtility.DisplayDialog(
                    "Alert!",
                    $"Are you sure you want to delete history {item.Id}?", "Yes",
                    "No"))
            {
                return;
            }

            try
            {
                await api.HistoryEndpoint.DeleteHistoryItemAsync(item);
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
            finally
            {
                FetchHistory();
            }
        }

        #endregion History

        #region GUI Utilities

        private static GUIContent GetHistoryItemLabel(HistoryItem item)
        {
            var id = item.Id;

            if (historyItemLabelCache.TryGetValue(id, out var label))
            {
                return label;
            }

            label = new GUIContent($"{item.Date} | {item.VoiceName}");
            historyItemLabelCache.TryAdd(id, label);

            return label;
        }

        private static IEnumerable<AudioClip> GetSampleClips(string filter)
        {
            if (voiceSampleCache.TryGetValue(filter, out var audioClips))
            {
                return audioClips;
            }

            var guids = AssetDatabase.FindAssets(filter);
            var sampleClips = new List<AudioClip>(guids.Length);
            sampleClips.AddRange(guids.Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<AudioClip>));
            voiceSampleCache.TryAdd(filter, sampleClips);
            return sampleClips;
        }

        private static void StartIndent(int levels)
        {
            for (var i = 0; i < levels; i++)
            {
                EditorGUI.indentLevel++;
            }
        }

        private static void EndIndent(int levels)
        {
            for (var i = 0; i < levels; i++)
            {
                EditorGUI.indentLevel--;
            }
        }

        #endregion GUI Utilities
    }
}
