using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace EasyAlarm.Services
{
    /// <summary>삭제해야 할 구글 일정 1건의 기록</summary>
    public class PendingDelete
    {
        public string EventId { get; set; } = string.Empty;
        public string CalendarId { get; set; } = string.Empty;
        public DateTime QueuedAt { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// '삭제 예정 명부' 담당. %appdata%\EasyAlarm\pending-deletes.json
    ///
    /// 왜 필요한가: 로컬 알람을 지우면 그 안의 구글 일정 번호도 함께 사라진다.
    /// 통신이 끊긴 상태에서 삭제하면 나중에 지울 방법이 없어지므로,
    /// 지우기 전에 번호를 이 명부에 먼저 적어둔다(영수증 선발행).
    /// </summary>
    public class PendingDeleteService
    {
        private readonly string _filePath;

        // 백그라운드 작업과 UI가 동시에 명부를 만질 수 있어 자물쇠로 보호
        private static readonly object _lock = new object();

        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        public PendingDeleteService()
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "EasyAlarm");

            Directory.CreateDirectory(folder);
            _filePath = Path.Combine(folder, "pending-deletes.json");
        }

        /// <summary>명부 전체를 읽어옵니다. 실패 시 빈 목록.</summary>
        public List<PendingDelete> LoadAll()
        {
            lock (_lock)
            {
                try
                {
                    if (!File.Exists(_filePath))
                    {
                        return new List<PendingDelete>();
                    }

                    string json = File.ReadAllText(_filePath);

                    if (string.IsNullOrWhiteSpace(json))
                    {
                        return new List<PendingDelete>();
                    }

                    return JsonSerializer.Deserialize<List<PendingDelete>>(json)
                           ?? new List<PendingDelete>();
                }
                catch (Exception ex)
                {
                    LogService.LogError("삭제 명부 읽기", ex);
                    return new List<PendingDelete>();
                }
            }
        }

        /// <summary>명부에 기록을 추가합니다. (같은 일정 번호는 중복 기록하지 않음)</summary>
        public void Add(string eventId, string calendarId)
        {
            if (string.IsNullOrWhiteSpace(eventId))
            {
                return;
            }

            lock (_lock)
            {
                try
                {
                    List<PendingDelete> list = LoadAllUnlocked();

                    if (list.Exists(p => p.EventId == eventId))
                    {
                        return;
                    }

                    list.Add(new PendingDelete
                    {
                        EventId = eventId,
                        CalendarId = calendarId,
                        QueuedAt = DateTime.Now
                    });

                    SaveUnlocked(list);
                }
                catch (Exception ex)
                {
                    LogService.LogError("삭제 명부 추가", ex);
                }
            }
        }

        /// <summary>처리 완료된 기록을 명부에서 지웁니다.</summary>
        public void Remove(string eventId)
        {
            lock (_lock)
            {
                try
                {
                    List<PendingDelete> list = LoadAllUnlocked();

                    if (list.RemoveAll(p => p.EventId == eventId) > 0)
                    {
                        SaveUnlocked(list);
                    }
                }
                catch (Exception ex)
                {
                    LogService.LogError("삭제 명부 정리", ex);
                }
            }
        }

        // ── 자물쇠 안에서만 쓰는 내부 도구 (중복 잠금 방지) ──

        private List<PendingDelete> LoadAllUnlocked()
        {
            if (!File.Exists(_filePath))
            {
                return new List<PendingDelete>();
            }

            string json = File.ReadAllText(_filePath);

            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<PendingDelete>();
            }

            return JsonSerializer.Deserialize<List<PendingDelete>>(json)
                   ?? new List<PendingDelete>();
        }

        private void SaveUnlocked(List<PendingDelete> list)
        {
            File.WriteAllText(_filePath, JsonSerializer.Serialize(list, _jsonOptions));
        }
    }
}