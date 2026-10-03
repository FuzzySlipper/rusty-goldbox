using System.Numerics;
using Rusty.Engine;
using RustyGoldbox.Core.Campaigns;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Modules;

namespace RustyGoldbox.Game.Presentation;

/// <summary>
/// The session's audio through Engine audio: each event's sound played once
/// on the Sfx bus, and one looping voice on the Music bus following the
/// campaign's music. Clips are opened once per asset content from the
/// module's bundle; the Engine keeps the bytes.
/// </summary>
internal sealed class GameAudio(IEngineContext engine, ModuleLibrary library) : IDisposable
{
    private readonly Dictionary<string, AudioClip?> _clips = [];

    /// <summary>One-shot sounds not yet finished, by signal, with their clips: a clip can't be released while one plays.</summary>
    private readonly Dictionary<ulong, AudioClip> _sounding = [];
    private ulong _lastFact;
    private AudioVoice? _music;
    private Definition? _playing;
    private ulong _signals;
    private (float Music, float Sound)? _volumes;

    /// <summary>Plays the sounds the session's latest commands brought, follows its music, and applies its volumes.</summary>
    public void Update(GameSession session)
    {
        if (_volumes != (session.MusicVolume, session.SoundVolume))
        {
            _volumes = (session.MusicVolume, session.SoundVolume);
            engine.Audio.SetBusVolume(new AudioBusVolumeRequest(AudioBus.Music, session.MusicVolume));
            engine.Audio.SetBusVolume(new AudioBusVolumeRequest(AudioBus.Sfx, session.SoundVolume));
        }

        Finished();
        foreach (Definition sound in session.TakeSounds())
        {
            if (session.Set is ModuleSet set && Clip(set, sound) is AudioClip clip)
            {
                AudioSignalHandle signal = engine.Audio.Emit(new AudioEmitRequest($"goldbox.sound.{++_signals}", Source(clip, AudioBus.Sfx, looping: false)));
                _sounding[signal.Value] = clip;
            }
        }

        // Music plays while a campaign runs; quitting or the title screen stops it.
        Definition? music = session.Runner is CampaignRunner runner ? runner.State.Music : null;
        if (music == _playing)
        {
            return;
        }

        StopMusic();
        if (music is not null && session.Set is ModuleSet loaded && Clip(loaded, music) is AudioClip track)
        {
            _music = engine.Audio.CreateVoice(Source(track, AudioBus.Music, looping: true));
            _playing = music;
        }
    }

    /// <summary>
    /// Releases the music and the clips. A clip whose sound is still playing
    /// can't be released; at shutdown the Engine ends it with the product.
    /// </summary>
    public void Dispose()
    {
        StopMusic();
        Finished();
        HashSet<AudioClip> playing = [.. _sounding.Values];
        foreach (AudioClip? clip in _clips.Values)
        {
            if (clip is not null && !playing.Contains(clip))
            {
                clip.Dispose();
            }
        }

        _clips.Clear();
    }

    /// <summary>Forgets the one-shot sounds the Engine reports finished (or failed).</summary>
    private void Finished()
    {
        foreach (AudioRealizationFact fact in engine.Audio.ReadRealization().Facts.Span)
        {
            if (fact.FactId <= _lastFact)
            {
                continue;
            }

            _lastFact = fact.FactId;
            _sounding.Remove(fact.SignalHandle);
        }
    }

    private void StopMusic()
    {
        _music?.Dispose();
        _music = null;
        _playing = null;
    }

    /// <summary>The asset's clip, or null when its module can't be opened.</summary>
    private AudioClip? Clip(ModuleSet set, Definition asset)
    {
        ModuleSource source = set.LoadOrder.First(loaded => loaded.Manifest.Id == asset.Module).Manifest.Source;
        string key = $"{asset.QualifiedId}@{source.Identity}";
        if (!_clips.TryGetValue(key, out AudioClip? clip))
        {
            string file = asset.Json.GetProperty("file").GetString()!;
            clip = library.ReadModule(source.Location, bundle =>
            {
                using ContentReference reference = bundle.OpenReference(file);
                return engine.Audio.OpenClipFromContent(new AudioClipFromContentRequest(reference));
            });
            _clips[key] = clip;
        }

        return clip;
    }

    /// <summary>A sound heard everywhere alike (no position, no panning).</summary>
    private static AudioSourceDescriptor Source(AudioClip clip, AudioBus bus, bool looping)
    {
        return new AudioSourceDescriptor(clip, bus, 1, 1, looping, 0, 1, AudioRolloff.Linear, 0, AudioEmitterKind.Global2d, Vector3.Zero, 0, Vector3.Zero);
    }
}
