namespace SO2RAccess
{
    /// <summary>
    /// Cooking master rhythm cues, timed by <see cref="CookingMasterHandler"/> on the
    /// game's own step frames: beat tick (CookTick.wav, 880 Hz), preview note tone
    /// (CookHit.wav, 1320 + 1760 Hz, 75 ms), beat (CookTick.wav, 880 Hz, 40 ms)
    /// and the listen marker
    /// (CookListen.wav, falling chirp). All synthesized; users can replace them in
    /// UserData\SO2RAccess\Sounds.
    /// </summary>
    public static partial class AudioCuePlayer
    {
        private static readonly MemoryCue _cookTickCue = new MemoryCue("cooking tick");
        private static readonly MemoryCue _cookHitCue = new MemoryCue("cooking hit");
        private static readonly MemoryCue _cookListenCue = new MemoryCue("cooking listen");

        /// <summary>Loads the beat tick, the note tone and the listen marker (user copy first, else bundled).</summary>
        public static void LoadCookingSounds(string tickFileName, string hitFileName, string listenFileName)
        {
            _cookTickCue.Load(tickFileName);
            _cookHitCue.Load(hitFileName);
            _cookListenCue.Load(listenFileName);
        }

        /// <summary>True when both cooking cues loaded.</summary>
        public static bool IsCookingSoundLoaded => _cookTickCue.IsLoaded && _cookHitCue.IsLoaded;

        /// <summary>Beat ticks sit under the note tones: half the cooking cue volume.</summary>
        private const float TickVolumeShare = 0.5f;

        /// <summary>Plays one beat tick, quieter than the note tone.</summary>
        public static void PlayCookingTick() => _cookTickCue.Play(ModSettings.CookingCueVolume * TickVolumeShare);

        /// <summary>Plays the note tone (a note of the pattern; press with it on your turn) at the cooking cue volume.</summary>
        public static void PlayCookingHit() => _cookHitCue.Play(ModSettings.CookingCueVolume);

        /// <summary>Falling chirp on the first beat of the preview: listen to the pattern.</summary>
        public static void PlayCookingListen() => _cookListenCue.Play(ModSettings.CookingCueVolume);

    }
}
