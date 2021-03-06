﻿using NAudio.CoreAudioApi;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EspionSpotify.AudioSessions
{
    public class SpotifyAudioSession : MainAudioSession, ISpotifyAudioSession
    {
        private const int SLEEP_VALUE = 50;
        private const int NUMBER_OF_SAMPLES = 3;

        private readonly Process _spytifyProcess;
        private readonly ICollection<int> _spotifyProcessesIds;
        public ICollection<AudioSessionControl> SpotifyAudioSessionControls { get; private set; }

        private SessionCollection GetSessionsAudioEndPointDevice => AudioEndPointDevice.AudioSessionManager.Sessions;
        private SessionCollection[] GetAllSessionsOfAudioEndPointDevices => AudioEndPointDevices.Select(x => x.AudioSessionManager.Sessions).ToArray();

        public SpotifyAudioSession(int? audioEndPointDeviceIndex)
        {
            AudioEndPointDeviceIndex = audioEndPointDeviceIndex;

            AudioMMDevices = new MMDeviceEnumerator();
            UpdateAudioEndPointDevices();

            _spotifyProcessesIds = SpotifyProcess.GetSpotifyProcesses().Select(x => x.Id).ToList();
            _spytifyProcess = Process.GetCurrentProcess();
            SpotifyAudioSessionControls = new List<AudioSessionControl>();
        }

        public async void SleepWhileTheSongEnds()
        {
            for (var times = 1000; await IsSpotifyCurrentlyPlaying() && times > 0; times -= SLEEP_VALUE * NUMBER_OF_SAMPLES)
            {
                await Task.Delay(SLEEP_VALUE);
            }
        }

        public async Task<bool> IsSpotifyCurrentlyPlaying()
        {
            var samples = new List<double>();

            for (var sample = 0; sample < NUMBER_OF_SAMPLES; sample++)
            {
                var spotifySoundValue = 0.0;
                await Task.Delay(SLEEP_VALUE);

                lock (SpotifyAudioSessionControls)
                {
                    foreach (var audioSession in SpotifyAudioSessionControls)
                    {
                        var soundValue = Math.Round(audioSession.AudioMeterInformation.MasterPeakValue * 100.0, 1);
                        if (soundValue == 0.0) continue;

                        spotifySoundValue = soundValue;
                    }
                }

                samples.Add(spotifySoundValue);
            }

            return samples.DefaultIfEmpty().Average() > 1.0;
        }

        public void SetSpotifyToMute(bool mute)
        {
            lock (SpotifyAudioSessionControls)
            {
                foreach (var audioSession in SpotifyAudioSessionControls)
                {
                    audioSession.SimpleAudioVolume.Mute = mute;
                }
            }
        }

        public async Task<bool> WaitSpotifyAudioSessionToStart(bool running)
        {
            if (await IsSpotifyPlayingOutsideDefaultAudioEndPoint(running))
            {
                return false;
            }

            var sessionAudioEndPointDevice = GetSessionsAudioEndPointDevice;

            for (var i = 0; i < sessionAudioEndPointDevice.Count; i++)
            {
                var currentAudioSessionControl = sessionAudioEndPointDevice[i];
                var currentProcessId = (int)currentAudioSessionControl.GetProcessID;
                if (!IsSpotifyAudioSessionControl(currentProcessId)) continue;

                return true;
            }

            return false;
        }

        public void SetSpotifyVolumeToHighAndOthersToMute(bool mute)
        {
            DefaultEndPointDevice.AudioSessionManager.RefreshSessions();

            var sessionAudioEndPointDevice = GetSessionsAudioEndPointDevice;
            
            for (var i = 0; i < sessionAudioEndPointDevice.Count; i++)
            {
                var currentAudioSessionControl = sessionAudioEndPointDevice[i];
                var currentProcessId = (int)currentAudioSessionControl.GetProcessID;

                if (currentProcessId.Equals(_spytifyProcess.Id)) continue;

                if (IsSpotifyAudioSessionControl(currentProcessId))
                {
                    SpotifyAudioSessionControls.Add(currentAudioSessionControl);

                    if (currentAudioSessionControl.SimpleAudioVolume.Volume < 1)
                    {
                        currentAudioSessionControl.SimpleAudioVolume.Volume = 1;
                    }
                }
                else if (!currentAudioSessionControl.SimpleAudioVolume.Mute.Equals(mute))
                {
                    currentAudioSessionControl.SimpleAudioVolume.Mute = mute;
                }
            }
        }

        private async Task<bool> IsSpotifyPlayingOutsideDefaultAudioEndPoint(bool running)
        {
            int? spotifyAudioSessionProcessId = null;

            while (running && spotifyAudioSessionProcessId == null && SpotifyProcess.GetSpotifyProcesses().Select(x => x.Id).Any())
            {
                var allSessionsAudioEndPointDevices = GetAllSessionsOfAudioEndPointDevices;

                foreach (var sessionAudioEndPointDevice in allSessionsAudioEndPointDevices)
                {
                    for (var i = 0; i < sessionAudioEndPointDevice.Count; i++)
                    {
                        var currentAudioSessionControl = sessionAudioEndPointDevice[i];
                        var currentProcessId = (int)currentAudioSessionControl.GetProcessID;
                        if (!IsSpotifyAudioSessionControl(currentProcessId)) continue;

                        spotifyAudioSessionProcessId = currentProcessId;
                    }
                }

                AudioMMDevices = new MMDeviceEnumerator();
                UpdateAudioEndPointDevices();
                await Task.Delay(300);
            }

            var sessionAudioSelectedEndPointDevice = GetSessionsAudioEndPointDevice;

            for (var i = 0; i < sessionAudioSelectedEndPointDevice.Count; i++)
            {
                var currentAudioSessionControl = sessionAudioSelectedEndPointDevice[i];
                var currentProcessId = (int)currentAudioSessionControl.GetProcessID;
                if (currentProcessId != spotifyAudioSessionProcessId) continue;

                return false;
            }

            return true;
        }

        private bool IsSpotifyAudioSessionControl(int processId) => _spotifyProcessesIds.Any(x => x == processId);
    }
}
