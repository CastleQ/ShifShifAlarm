using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using EasyAlarm.Models;

namespace EasyAlarm.Services
{
    /// <summary>
    /// 알람 데이터를 JSON 파일에 저장/불러오기/삭제하는 창고지기
    /// </summary>
    public class StorageService
    {
        private readonly string _folderPath;
        private readonly string _filePath;

        // 완료된 알람의 보존 기간. 이보다 오래된 완료 항목은 시작 시 자동 파기됩니다.
        private const int CompletedRetentionDays = 30;

        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        public StorageService()
        {
            _folderPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "EasyAlarm");

            _filePath = Path.Combine(_folderPath, "alarms.json");

            Directory.CreateDirectory(_folderPath);
        }

        /// <summary>
        /// 서류함에서 모든 알람(예정 + 완료)을 꺼내옵니다.
        /// 파일이 없거나 내용이 깨져 있어도 프로그램이 죽지 않고 빈 목록을 돌려줍니다.
        /// </summary>
        public List<AlarmModel> LoadAlarms()
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    return new List<AlarmModel>();
                }

                string json = File.ReadAllText(_filePath);

                if (string.IsNullOrWhiteSpace(json))
                {
                    return new List<AlarmModel>();
                }

                List<AlarmModel>? alarms = JsonSerializer.Deserialize<List<AlarmModel>>(json);
                return alarms ?? new List<AlarmModel>();
            }
            catch (Exception)
            {
                return new List<AlarmModel>();
            }
        }

        /// <summary>
        /// 알람 목록 전체를 서류함에 저장합니다. (기존 내용은 새 내용으로 교체)
        /// </summary>
        public void SaveAlarms(List<AlarmModel> alarms)
        {
            string json = JsonSerializer.Serialize(alarms, _jsonOptions);
            File.WriteAllText(_filePath, json);
        }

        /// <summary>
        /// 새 알람 1개를 서류함에 추가합니다.
        /// </summary>
        public void AddAlarm(AlarmModel alarm)
        {
            List<AlarmModel> alarms = LoadAlarms();
            alarms.Add(alarm);
            SaveAlarms(alarms);
        }

        /// <summary>
        /// 고유번호(Id)가 일치하는 알람 1개를 서류함에서 삭제합니다.
        /// (예정/완료 모두에 사용 — 목록 창의 개별 [삭제] 버튼용)
        /// </summary>
        public void DeleteAlarm(Guid id)
        {
            List<AlarmModel> alarms = LoadAlarms();
            alarms.RemoveAll(alarm => alarm.Id == id);
            SaveAlarms(alarms);
        }

        /// <summary>
        /// 완료된 알람을 전부 파기합니다. (목록 창의 [전체삭제] 버튼용)
        /// 예정된 알람은 건드리지 않습니다.
        /// </summary>
        public void DeleteAllCompleted()
        {
            List<AlarmModel> alarms = LoadAlarms();
            alarms.RemoveAll(alarm => alarm.IsCompleted);
            SaveAlarms(alarms);
        }

        /// <summary>
        /// 보존 기간(30일)이 지난 완료 알람을 자동 파기합니다.
        /// 프로그램 시작 시 1회 호출됩니다. (업계 관례: 완료 항목 자동 정리)
        /// </summary>
        public void CleanupOldCompleted()
        {
            DateTime cutoff = DateTime.Now.AddDays(-CompletedRetentionDays);

            List<AlarmModel> alarms = LoadAlarms();
            int removed = alarms.RemoveAll(alarm =>
                alarm.IsCompleted &&
                alarm.CompletedAt.HasValue &&
                alarm.CompletedAt.Value < cutoff);

            if (removed > 0)
            {
                SaveAlarms(alarms);
            }
        }
    }
}