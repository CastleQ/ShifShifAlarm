using System;
using System.IO;
using System.Text.Json;
using EasyAlarm.Models;

namespace EasyAlarm.Services
{
    /// <summary>
    /// 환경설정 보관 담당. alarms.json 옆에 settings.json으로 저장합니다.
    /// 파일이 없거나 깨져 있어도 기본값으로 조용히 시작합니다.
    /// </summary>
    public class SettingsService
    {
        private readonly string _filePath;

        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        public SettingsService()
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "EasyAlarm");

            Directory.CreateDirectory(folder);
            _filePath = Path.Combine(folder, "settings.json");
        }

        /// <summary>설정을 읽어옵니다. 실패 시 기본값.</summary>
        public AppSettings Load()
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    return new AppSettings();
                }

                string json = File.ReadAllText(_filePath);

                if (string.IsNullOrWhiteSpace(json))
                {
                    return new AppSettings();
                }

                AppSettings? settings = JsonSerializer.Deserialize<AppSettings>(json);
                return settings ?? new AppSettings();
            }
            catch (Exception ex)
            {
                LogService.LogError("설정 읽기", ex);
                return new AppSettings();
            }
        }

        /// <summary>설정을 저장합니다.</summary>
        public void Save(AppSettings settings)
        {
            try
            {
                File.WriteAllText(_filePath, JsonSerializer.Serialize(settings, _jsonOptions));
            }
            catch (Exception ex)
            {
                LogService.LogError("설정 저장", ex);
            }
        }
    }
}