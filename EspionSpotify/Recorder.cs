using System;
using System.IO;
using System.IO.Abstractions;
using System.Threading.Tasks;
using EspionSpotify.Enums;
using EspionSpotify.Models;
using NAudio.Lame;
using NAudio.Wave;

namespace EspionSpotify
{
    internal class Recorder : IRecorder
    {
        public int CountSeconds { get; set; }
        public bool Running { get; set; }

        private readonly UserSettings _userSettings;
        private readonly Track _track;
        private readonly IFrmEspionSpotify _form;
        private OutputFile _currentOutputFile;
        private WasapiLoopbackCapture _waveIn;
        private Stream _writer;
        private readonly FileManager _fileManager;
        private readonly IFileSystem _fileSystem;

        public Recorder() { }

        public Recorder(IFrmEspionSpotify espionSpotifyForm, UserSettings userSettings, Track track, IFileSystem fileSystem)
        {
            _form = espionSpotifyForm;
            _fileSystem = fileSystem;
            _track = track;
            _userSettings = userSettings;
            _fileManager = new FileManager(_userSettings, _track, fileSystem);
        }

        public async void Run()
        {
            Running = true;
            await Task.Delay(50);
            _waveIn = new WasapiLoopbackCapture(_userSettings.SpotifyAudioSession.AudioEndPointDevice);

            _waveIn.DataAvailable += WaveIn_DataAvailable;
            _waveIn.RecordingStopped += WaveIn_RecordingStopped;

            _currentOutputFile = _fileManager.GetOutputFile(_userSettings.OutputPath);

            _writer = new WaveFileWriter(_currentOutputFile.ToPendingFileString(), _waveIn.WaveFormat);
            if (_writer == null)
            {
                Running = false;
                return;
            }

            _waveIn.StartRecording();
            _form.WriteIntoConsole(I18nKeys.LogRecording, _currentOutputFile.File);

            while (Running)
            {
                await Task.Delay(50);
            }

            _waveIn.StopRecording();
        }

        private async void WaveIn_DataAvailable(object sender, WaveInEventArgs e)
        {
            // TODO: add buffer handler from argument: issue #100
            if (_writer != null) await _writer.WriteAsync(e.Buffer, 0, e.BytesRecorded);
        }

        private async void WaveIn_RecordingStopped(object sender, StoppedEventArgs e)
        {
            if (_writer != null)
            {
                await _writer.FlushAsync();
                _writer.Dispose();
                _waveIn.Dispose();
            }

            if (CountSeconds < _userSettings.MinimumRecordedLengthSeconds)
            {
                _form.WriteIntoConsole(I18nKeys.LogDeleting, _currentOutputFile.File, _userSettings.MinimumRecordedLengthSeconds);
                _fileManager.DeleteFile(_currentOutputFile.ToPendingFileString());
                return;
            }

            var length = TimeSpan.FromSeconds(CountSeconds).ToString(@"mm\:ss");
            _form.WriteIntoConsole(I18nKeys.LogRecorded, _track.ToString(), length);

            await UpdateOutputFileBasedOnMediaFormat();    
        }

        private async Task UpdateOutputFileBasedOnMediaFormat()
        {
            switch (_userSettings.MediaFormat)
            {
                case MediaFormat.Wav:
                    _fileManager.Rename(_currentOutputFile.ToPendingFileString(), _currentOutputFile.ToString());
                    return;
                case MediaFormat.Mp3:
                    using (var reader = new WaveFileReader(_currentOutputFile.ToPendingFileString()))
                    {
                        using (var mp3writer = new LameMP3FileWriter(_currentOutputFile.ToTranscodingToMP3String(), _waveIn.WaveFormat, _userSettings.Bitrate))
                        {
                            await reader.CopyToAsync(mp3writer);
                            if (mp3writer != null) await mp3writer.FlushAsync();
                        }
                    }

                    _fileManager.DeleteFile(_currentOutputFile.ToPendingFileString());
                    _fileManager.Rename(_currentOutputFile.ToTranscodingToMP3String(), _currentOutputFile.ToString());

                    var mp3TagsInfo = new MediaTags.MP3Tags()
                    {
                        Track = _track,
                        OrderNumberInMediaTagEnabled = _userSettings.OrderNumberInMediaTagEnabled,
                        Count = _userSettings.OrderNumber,
                        CurrentFile = _currentOutputFile.ToString()
                    };
                    await mp3TagsInfo.SaveMediaTags();

                    return;
                default:
                    return;
            }
        }

        public static bool TestFileWriter(IFrmEspionSpotify form, UserSettings settings)
        {
            var waveIn = new WasapiLoopbackCapture(settings.SpotifyAudioSession.AudioEndPointDevice);
            switch (settings.MediaFormat)
            {
                case MediaFormat.Mp3:
                    try
                    {
                        using (var writer = new LameMP3FileWriter(new MemoryStream(), waveIn.WaveFormat, settings.Bitrate)) return true;
                    }
                    catch (ArgumentException ex)
                    {
                        LogLameMP3FileWriterArgumentException(form, ex, settings.OutputPath);
                        return false;
                    }
                    catch (Exception ex)
                    {
                        LogLameMP3FileWriterException(form, ex);
                        return false;
                    }
                case MediaFormat.Wav:
                    try
                    {
                        using (var writer = new WaveFileWriter(new MemoryStream(), waveIn.WaveFormat)) return true;
                    }
                    catch (Exception ex)
                    {
                        form.UpdateIconSpotify(true, false);
                        form.WriteIntoConsole(I18nKeys.LogUnknownException, ex.Message);
                        Console.WriteLine(ex.Message);
                        return false;
                    }
                default:
                    return false;
            }
        }

        private static void LogLameMP3FileWriterArgumentException(IFrmEspionSpotify form, ArgumentException ex, string outputPath)
        {
            var resource = I18nKeys.LogUnknownException;
            var args = ex.Message;

            if (!Directory.Exists(outputPath))
            {
                resource = I18nKeys.LogInvalidOutput;
            }
            else if (ex.Message.StartsWith("Unsupported Sample Rate"))
            {
                resource = I18nKeys.LogUnsupportedRate;
            }
            else if (ex.Message.StartsWith("Access to the path"))
            {
                resource = I18nKeys.LogNoAccessOutput;
            }
            else if (ex.Message.StartsWith("Unsupported number of channels"))
            {
                var numberOfChannels = ex.Message.Length > 32 ? ex.Message.Remove(0, 31) : "?";
                var indexOfBreakLine = numberOfChannels.IndexOf("\r\n");
                numberOfChannels = numberOfChannels.Substring(0, indexOfBreakLine != -1 ? indexOfBreakLine : 0);
                resource = I18nKeys.LogUnsupportedNumberChannels;
                args = numberOfChannels;
            }

            form.UpdateIconSpotify(true, false);
            form.WriteIntoConsole(resource, args);
        }

        private static void LogLameMP3FileWriterException(IFrmEspionSpotify form, Exception ex)
        {
            if (ex.Message.Contains("Unable to load DLL"))
            {
                form.WriteIntoConsole(I18nKeys.LogMissingDlls);
            }
            else
            {
                form.WriteIntoConsole(I18nKeys.LogUnknownException, ex.Message);
            }

            form.UpdateIconSpotify(true, false);
            Console.WriteLine(ex.Message);
        }
}
}
