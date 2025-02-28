// Licensed under the MIT License. See LICENSE in the project root for license information.

using ElevenLabs.Voices;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace ElevenLabs.Voice.Tests
{
    internal class Test_Fixture_02_VoicesEndpoint
    {
        [Test]
        public async Task Test_01_GetVoices()
        {
            var api = new ElevenLabsClient(ElevenLabsAuthentication.Default.LoadFromEnvironment());
            Assert.NotNull(api.VoicesEndpoint);
            var results = await api.VoicesEndpoint.GetAllVoicesAsync();
            Assert.NotNull(results);
            Assert.IsNotEmpty(results);

            foreach (var voice in results)
            {
                Debug.Log($"{voice.Id} | {voice.Name} | similarity boost: {voice.Settings?.SimilarityBoost} | stability: {voice.Settings?.Stability}");
            }
        }

        [Test]
        public async Task Test_02_GetDefaultVoiceSettings()
        {
            var api = new ElevenLabsClient(ElevenLabsAuthentication.Default.LoadFromEnvironment());
            Assert.NotNull(api.VoicesEndpoint);
            var result = await api.VoicesEndpoint.GetDefaultVoiceSettingsAsync();
            Assert.NotNull(result);
            Debug.Log($"stability: {result.Stability} | similarity boost: {result.SimilarityBoost}");
        }

        [Test]
        public async Task Test_03_GetVoice()
        {
            var api = new ElevenLabsClient(ElevenLabsAuthentication.Default.LoadFromEnvironment());
            Assert.NotNull(api.VoicesEndpoint);
            var results = await api.VoicesEndpoint.GetAllVoicesAsync();
            Assert.NotNull(results);
            Assert.IsNotEmpty(results);
            var voiceToGet = results.OrderBy(voice => voice.Name).FirstOrDefault();
            var result = await api.VoicesEndpoint.GetVoiceAsync(voiceToGet);
            Assert.NotNull(result);
            Debug.Log($"{result.Id} | {result.Name} | {result.PreviewUrl}");
        }

        [Test]
        public async Task Test_04_EditVoiceSettings()
        {
            var api = new ElevenLabsClient(ElevenLabsAuthentication.Default.LoadFromEnvironment());
            Assert.NotNull(api.VoicesEndpoint);
            var results = await api.VoicesEndpoint.GetAllVoicesAsync();
            Assert.NotNull(results);
            Assert.IsNotEmpty(results);
            var voice = results.FirstOrDefault();
            var result = await api.VoicesEndpoint.EditVoiceSettingsAsync(voice, new VoiceSettings(0.7f, 0.7f));
            Assert.NotNull(result);
            Assert.IsTrue(result);
            var updatedVoice = await api.VoicesEndpoint.GetVoiceAsync(voice);
            Assert.NotNull(updatedVoice);
            Debug.Log($"{updatedVoice.Id} | similarity boost: {updatedVoice.Settings?.SimilarityBoost} | stability: {updatedVoice.Settings?.Stability}");
            var defaultVoiceSettings = await api.VoicesEndpoint.GetDefaultVoiceSettingsAsync();
            Assert.NotNull(defaultVoiceSettings);
            var defaultResult = await api.VoicesEndpoint.EditVoiceSettingsAsync(voice, defaultVoiceSettings);
            Assert.NotNull(defaultResult);
            Assert.IsTrue(defaultResult);
        }

        [Test]
        public async Task Test_05_AddVoice()
        {
            var api = new ElevenLabsClient(ElevenLabsAuthentication.Default.LoadFromEnvironment());
            Assert.NotNull(api.VoicesEndpoint);
            var testLabels = new Dictionary<string, string>
            {
                { "accent", "american" }
            };
            var clipPath = AssetDatabase.GUIDToAssetPath("96e9fdf73bc7a944f93886694973b90e");
            var result = await api.VoicesEndpoint.AddVoiceAsync("Test Voice", new[] { clipPath }, testLabels);
            Assert.NotNull(result);
            Debug.Log($"{result.Name}");
            Assert.IsNotEmpty(result.Samples);
        }

        [Test]
        public async Task Test_06_EditVoice()
        {
            var api = new ElevenLabsClient(ElevenLabsAuthentication.Default.LoadFromEnvironment());
            Assert.NotNull(api.VoicesEndpoint);
            var results = await api.VoicesEndpoint.GetAllVoicesAsync();
            Assert.NotNull(results);
            Assert.IsNotEmpty(results);
            var voiceToEdit = results.FirstOrDefault(voice => voice.Name.Contains("Test Voice"));
            Assert.NotNull(voiceToEdit);
            var testLabels = new Dictionary<string, string>
            {
                { "accent", "american" },
                { "key", "value" }
            };
            var clipPath = AssetDatabase.GUIDToAssetPath("96e9fdf73bc7a944f93886694973b90e");
            var result = await api.VoicesEndpoint.EditVoiceAsync(voiceToEdit, new[] { clipPath }, testLabels);
            Assert.NotNull(result);
            Assert.IsTrue(result);
        }

        [Test]
        public async Task Test_07_GetVoiceSample()
        {
            var api = new ElevenLabsClient(ElevenLabsAuthentication.Default.LoadFromEnvironment());
            Assert.NotNull(api.VoicesEndpoint);
            var results = await api.VoicesEndpoint.GetAllVoicesAsync();
            Assert.NotNull(results);
            Assert.IsNotEmpty(results);
            var voice = results.FirstOrDefault(voice => voice.Name.Contains("Test Voice"));
            Assert.NotNull(voice);
            var updatedVoice = await api.VoicesEndpoint.GetVoiceAsync(voice);
            Assert.NotNull(updatedVoice);
            Assert.IsNotEmpty(updatedVoice.Samples);
            var sample = updatedVoice.Samples.FirstOrDefault();
            Assert.NotNull(sample);
            var result = await api.VoicesEndpoint.GetVoiceSampleAsync(updatedVoice, updatedVoice.Samples.FirstOrDefault());
            Assert.NotNull(result);
        }

        [Test]
        public async Task Test_08_DeleteVoiceSample()
        {
            var api = new ElevenLabsClient(ElevenLabsAuthentication.Default.LoadFromEnvironment());
            Assert.NotNull(api.VoicesEndpoint);
            var results = await api.VoicesEndpoint.GetAllVoicesAsync();
            Assert.NotNull(results);
            Assert.IsNotEmpty(results);
            var voice = results.FirstOrDefault(voice => voice.Name.Contains("Test Voice"));
            Assert.NotNull(voice);
            var updatedVoice = await api.VoicesEndpoint.GetVoiceAsync(voice);
            Assert.NotNull(updatedVoice);
            Assert.IsNotEmpty(updatedVoice.Samples);
            var sample = updatedVoice.Samples.FirstOrDefault();
            Assert.NotNull(sample);
            var result = await api.VoicesEndpoint.DeleteVoiceSampleAsync(updatedVoice, sample);
            Assert.NotNull(result);
            Assert.IsTrue(result);
        }

        [Test]
        public async Task Test_09_DeleteVoice()
        {
            var api = new ElevenLabsClient(ElevenLabsAuthentication.Default.LoadFromEnvironment());
            Assert.NotNull(api.VoicesEndpoint);
            var results = await api.VoicesEndpoint.GetAllVoicesAsync();
            Assert.NotNull(results);
            Assert.IsNotEmpty(results);
            var voicesToDelete = results.Where(voice => voice.Name.Contains("Test Voice")).ToList();
            Assert.NotNull(voicesToDelete);
            Assert.IsNotEmpty(voicesToDelete);

            foreach (var voice in voicesToDelete)
            {
                var result = await api.VoicesEndpoint.DeleteVoiceAsync(voice);
                Assert.NotNull(result);
                Assert.IsTrue(result);
            }
        }
    }
}
