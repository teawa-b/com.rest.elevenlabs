// Licensed under the MIT License. See LICENSE in the project root for license information.

using ElevenLabs.VoiceGeneration;
using Newtonsoft.Json;
using NUnit.Framework;
using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace ElevenLabs.Voice.Tests
{
    internal class Test_Fixture_05_VoiceGeneration
    {
        [Test]
        public async Task Test_01_GetVoiceGenerationOptions()
        {
            var api = new ElevenLabsClient(ElevenLabsAuthentication.Default.LoadFromEnvironment());
            Assert.NotNull(api.VoiceGenerationEndpoint);
            var options = await api.VoiceGenerationEndpoint.GetVoiceGenerationOptionsAsync();
            Assert.NotNull(options);
            Debug.Log(JsonConvert.SerializeObject(options));
        }

        [Test]
        public async Task Test_02_GenerateVoice()
        {
            var api = new ElevenLabsClient(ElevenLabsAuthentication.Default.LoadFromEnvironment());
            Assert.NotNull(api.VoiceGenerationEndpoint);
            var options = await api.VoiceGenerationEndpoint.GetVoiceGenerationOptionsAsync();
            var generateRequest = new GeneratedVoiceRequest("First we thought the PC was a calculator. Then we found out how to turn numbers into letters and we thought it was a typewriter.", options.Genders.FirstOrDefault(), options.Accents.FirstOrDefault(), options.Ages.FirstOrDefault());
            var (generatedVoiceId, audioClip) = await api.VoiceGenerationEndpoint.GenerateVoiceAsync(generateRequest);
            Debug.Log(generatedVoiceId);
            Assert.NotNull(audioClip);
            var createVoiceRequest = new CreateVoiceRequest("Test Voice Lab Create Voice", generatedVoiceId);
            Assert.NotNull(createVoiceRequest);
            var result = await api.VoiceGenerationEndpoint.CreateVoiceAsync(createVoiceRequest);
            Assert.NotNull(result);
            Debug.Log(result.Id);
            var deleteResult = await api.VoicesEndpoint.DeleteVoiceAsync(result.Id);
            Assert.NotNull(deleteResult);
            Assert.IsTrue(deleteResult);
        }
    }
}
