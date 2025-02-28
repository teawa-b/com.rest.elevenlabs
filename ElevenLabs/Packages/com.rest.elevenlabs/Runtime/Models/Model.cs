// Licensed under the MIT License. See LICENSE in the project root for license information.

using Newtonsoft.Json;
using System.Collections.Generic;
using UnityEngine.Scripting;

namespace ElevenLabs.Models
{
    [Preserve]
    public sealed class Model
    {
        [Preserve]
        public Model(string id)
        {
            Id = id;
        }

        [Preserve]
        [JsonConstructor]
        public Model(
            [JsonProperty("model_id")] string id,
            [JsonProperty("name")] string name,
            [JsonProperty("description")] string description,
            [JsonProperty("can_be_finetuned")] bool canBeFineTuned,
            [JsonProperty("can_do_text_to_speech")] bool canDoTextToSpeech,
            [JsonProperty("can_do_voice_conversion")] bool canDoVoiceConversion,
            [JsonProperty("token_cost_factor")] double tokenCostFactor,
            [JsonProperty("languages")] IReadOnlyList<Language> languages)
        {
            Id = id;
            Name = name;
            Description = description;
            CanBeFineTuned = canBeFineTuned;
            CanDoTextToSpeech = canDoTextToSpeech;
            CanDoVoiceConversion = canDoVoiceConversion;
            TokenCostFactor = tokenCostFactor;
            Languages = languages;
        }

        [Preserve]
        [JsonProperty("model_id")]
        public string Id { get; }

        [Preserve]
        [JsonProperty("name")]
        public string Name { get; }

        [Preserve]
        [JsonProperty("description")]
        public string Description { get; }

        [Preserve]
        [JsonProperty("can_be_finetuned")]
        public bool CanBeFineTuned { get; }

        [Preserve]
        [JsonProperty("can_do_text_to_speech")]
        public bool CanDoTextToSpeech { get; }

        [Preserve]
        [JsonProperty("can_do_voice_conversion")]
        public bool CanDoVoiceConversion { get; }

        [Preserve]
        [JsonProperty("token_cost_factor")]
        public double TokenCostFactor { get; }

        [Preserve]
        [JsonProperty("languages")]
        public IReadOnlyList<Language> Languages { get; }

        [Preserve]
        public static implicit operator string(Model model) => model.ToString();

        [Preserve]
        public override string ToString() => Id;

        #region Predefined Models

        [Preserve]
        [JsonIgnore]
        public static Model MonoLingualV1 { get; } = new Model("eleven_monolingual_v1");

        [Preserve]
        [JsonIgnore]
        public static Model MultiLingualV1 { get; } = new Model("eleven_multilingual_v1");

        #endregion Predefined Models
    }
}
