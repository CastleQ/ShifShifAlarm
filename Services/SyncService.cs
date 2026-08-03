using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EasyAlarm.Models;

namespace EasyAlarm.Services
{
    /// <summary>
    /// 구글 캘린더 동기화 배달부.
    /// 등록·삭제 모두 로컬에서 즉시 끝내고, 구글 통신은 이 배달부가 백그라운드로 처리합니다.
    /// 실패분은 시작 시·주기적으로 자동 재시도됩니다.
    /// </summary>
    public class SyncService
    {
        private readonly StorageService _storage;
        private readonly SettingsService _settings;
        private readonly GoogleAuthService _auth;
        private readonly GoogleCalendarService _calendar;
        private readonly PendingDeleteService _pending;

        // 같은 작업이 동시에 여러 번 돌지 않도록 하는 통행권 (각각 한 번에 1개만)
        private readonly SemaphoreSlim _deleteGate = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _retryGate = new SemaphoreSlim(1, 1);

        /// <summary>같은 알람을 너무 자주 재시도하지 않도록 두는 최소 간격</summary>
        private static readonly TimeSpan RetryCooldown = TimeSpan.FromMinutes(3);

        /// <summary>한 번에 재시도할 최대 건수 (통신 폭주 방지)</summary>
        private const int MaxRetryPerPass = 10;

        /// <summary>동기화 결과가 장부에 기록된 뒤 울리는 종 (목록 창 갱신용)</summary>
        public event Action? SyncStateChanged;

        public SyncService(StorageService storage, SettingsService settings)
        {
            _storage = storage;
            _settings = settings;
            _auth = new GoogleAuthService();
            _calendar = new GoogleCalendarService(_auth);
            _pending = new PendingDeleteService();
        }

        /// <summary>업로드 준비 상태 (연동 켜짐 + 인증 파일 + 로그인 완료)</summary>
        public bool IsReady()
        {
            AppSettings settings = _settings.Load();
            return settings.GoogleSyncEnabled && _auth.HasSecretFile && _auth.IsConnected;
        }

        /// <summary>
        /// 삭제 처리 가능 상태.
        /// ⚠️ 업로드와 달리 연동 스위치를 보지 않습니다 —
        /// 이미 올라간 일정은 연동을 끈 뒤에도 정리되어야 하기 때문입니다.
        /// </summary>
        private bool CanDelete()
        {
            return _auth.HasSecretFile && _auth.IsConnected;
        }

        // ─────────────── 업로드 ───────────────

        /// <summary>알람 1건을 구글 캘린더로 보냅니다. (즉시 반환)</summary>
        public void QueueUpload(AlarmModel alarm)
        {
            if (!IsReady())
            {
                _storage.UpdateSyncResult(alarm.Id, null, SyncState.Skipped);
                return;
            }

            // 구글이 지원하지 않는 형식(분·시간 간격 반복)은 시도하지 않고 제외 처리
            if (!GoogleCalendarService.IsSyncable(alarm))
            {
                _storage.UpdateSyncResult(alarm.Id, null, SyncState.Skipped);
                return;
            }

            _storage.UpdateSyncResult(alarm.Id, null, SyncState.Pending);

            _ = Task.Run(() => UploadAsync(alarm));
        }

        private async Task UploadAsync(AlarmModel alarm)
        {
            try
            {
                AppSettings settings = _settings.Load();
                string eventId = await _calendar.CreateEventAsync(alarm, settings.GoogleCalendarId);

                _storage.UpdateSyncResult(alarm.Id, eventId, SyncState.Synced);
            }
            catch (Exception ex)
            {
                LogService.LogError("구글 일정 생성", ex);
                _storage.UpdateSyncResult(alarm.Id, null, SyncState.Failed);
            }
            finally
            {
                SyncStateChanged?.Invoke();
            }
        }

        /// <summary>
        /// 아직 구글에 올라가지 못한 알람들을 다시 시도합니다.
        /// 별도 명부가 필요 없는 이유: 장부의 Sync 표시(실패·대기)가 곧 재시도 목록입니다.
        /// </summary>
        public void RetryFailedUploads()
        {
            _ = Task.Run(ProcessFailedUploadsAsync);
        }

        private async Task ProcessFailedUploadsAsync()
        {
            if (!IsReady())
            {
                return;
            }

            // 이미 재시도가 돌고 있으면 이번 호출은 건너뛴다
            if (!await _retryGate.WaitAsync(0))
            {
                return;
            }

            try
            {
                DateTime now = DateTime.Now;

                List<AlarmModel> targets = _storage.LoadAlarms()
                    .Where(a => !a.IsCompleted)
                    .Where(a => string.IsNullOrWhiteSpace(a.GoogleEventId))
                    .Where(a => a.Sync == SyncState.Failed || a.Sync == SyncState.Pending)
                    .Where(a => GoogleCalendarService.IsSyncable(a))
                    .Where(a => !a.SyncAttemptedAt.HasValue ||
                                now - a.SyncAttemptedAt.Value > RetryCooldown)
                    .Take(MaxRetryPerPass)
                    .ToList();

                if (targets.Count == 0)
                {
                    return;
                }

                AppSettings settings = _settings.Load();

                foreach (AlarmModel alarm in targets)
                {
                    // 이미 시각이 지난 일반 알람은 캘린더에 올려도 알림이 오지 않으므로 제외
                    if (!alarm.IsUntimed &&
                        alarm.Repeat == RepeatType.None &&
                        alarm.AlarmTime < now)
                    {
                        _storage.UpdateSyncResult(alarm.Id, null, SyncState.Skipped);
                        continue;
                    }

                    try
                    {
                        string eventId = await _calendar.CreateEventAsync(alarm, settings.GoogleCalendarId);
                        _storage.UpdateSyncResult(alarm.Id, eventId, SyncState.Synced);
                    }
                    catch (Exception ex)
                    {
                        LogService.LogError("구글 일정 재시도", ex);
                        _storage.UpdateSyncResult(alarm.Id, null, SyncState.Failed);
                    }
                }

                SyncStateChanged?.Invoke();
            }
            finally
            {
                _retryGate.Release();
            }
        }

        // ─────────────── 삭제 ───────────────

        /// <summary>
        /// 알람 1건의 구글 일정 삭제를 예약합니다.
        /// ⚠️ 반드시 로컬 삭제 '전에' 호출해야 합니다 (일정 번호가 살아있는 동안 명부에 적어야 하므로).
        /// </summary>
        public void QueueDelete(AlarmModel alarm)
        {
            if (string.IsNullOrWhiteSpace(alarm.GoogleEventId))
            {
                return;
            }

            AppSettings settings = _settings.Load();
            _pending.Add(alarm.GoogleEventId, settings.GoogleCalendarId);

            _ = Task.Run(ProcessPendingDeletesAsync);
        }

        /// <summary>여러 알람의 구글 일정 삭제를 한 번에 예약합니다. (전체삭제용)</summary>
        public void QueueDeleteMany(IEnumerable<AlarmModel> alarms)
        {
            AppSettings settings = _settings.Load();
            bool any = false;

            foreach (AlarmModel alarm in alarms)
            {
                if (!string.IsNullOrWhiteSpace(alarm.GoogleEventId))
                {
                    _pending.Add(alarm.GoogleEventId, settings.GoogleCalendarId);
                    any = true;
                }
            }

            if (any)
            {
                _ = Task.Run(ProcessPendingDeletesAsync);
            }
        }

        /// <summary>명부에 남은 삭제 건을 처리합니다. (시작 시 호출 → 지난 실패분 자동 정리)</summary>
        public void RetryPendingDeletes()
        {
            _ = Task.Run(ProcessPendingDeletesAsync);
        }

        private async Task ProcessPendingDeletesAsync()
        {
            if (!CanDelete())
            {
                return;
            }

            if (!await _deleteGate.WaitAsync(0))
            {
                return;
            }

            try
            {
                List<PendingDelete> list = _pending.LoadAll();

                foreach (PendingDelete item in list)
                {
                    try
                    {
                        await _calendar.DeleteEventAsync(item.EventId, item.CalendarId);
                        _pending.Remove(item.EventId);
                    }
                    catch (Exception ex)
                    {
                        LogService.LogError($"구글 일정 삭제({item.EventId})", ex);
                    }
                }
            }
            finally
            {
                _deleteGate.Release();
            }
        }
    }
}