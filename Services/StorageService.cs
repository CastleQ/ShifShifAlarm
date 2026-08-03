using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using EasyAlarm.Models;

namespace EasyAlarm.Services
{
    /// <summary>알람 데이터를 JSON 파일에 저장/불러오기/삭제하는 창고지기</summary>
    public class StorageService
    {
        private readonly string _folderPath;
        private readonly string _filePath;

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

        /// <summary>서류함에서 모든 알람을 꺼내옵니다. 실패 시 빈 목록.</summary>
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

        /// <summary>알람 목록 전체를 저장합니다.</summary>
        public void SaveAlarms(List<AlarmModel> alarms)
        {
            string json = JsonSerializer.Serialize(alarms, _jsonOptions);
            File.WriteAllText(_filePath, json);
        }

        /// <summary>새 알람 1개를 추가합니다.</summary>
        public void AddAlarm(AlarmModel alarm)
        {
            List<AlarmModel> alarms = LoadAlarms();
            alarms.Add(alarm);
            SaveAlarms(alarms);
        }

        /// <summary>고유번호가 일치하는 알람 1개를 삭제합니다.</summary>
        public void DeleteAlarm(Guid id)
        {
            List<AlarmModel> alarms = LoadAlarms();
            alarms.RemoveAll(alarm => alarm.Id == id);
            SaveAlarms(alarms);
        }

        /// <summary>완료된 알람을 전부 파기합니다. (전체삭제 버튼용)</summary>
        public void DeleteAllCompleted()
        {
            List<AlarmModel> alarms = LoadAlarms();
            alarms.RemoveAll(alarm => alarm.IsCompleted);
            SaveAlarms(alarms);
        }

        /// <summary>보존 기간(30일)이 지난 완료 알람을 자동 파기합니다.</summary>
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

        /// <summary>
        /// 특정 알람의 구글 동기화 결과를 기록합니다.
        /// (동기화는 백그라운드에서 진행되므로, 결과가 나오는 대로 장부에 도장을 찍는 용도)
        /// </summary>
        public void UpdateSyncResult(Guid id, string? googleEventId, SyncState state)
        {
            try
            {
                List<AlarmModel> alarms = LoadAlarms();
                AlarmModel? target = alarms.Find(a => a.Id == id);

                if (target == null)
                {
                    return;   // 그 사이 삭제된 알람 → 조용히 무시
                }

                if (googleEventId != null)
                {
                    target.GoogleEventId = googleEventId;
                }

                target.Sync = state;
                target.SyncAttemptedAt = DateTime.Now;

                SaveAlarms(alarms);
            }
            catch (Exception ex)
            {
                LogService.LogError("동기화 결과 기록", ex);
            }
        }
    }
}