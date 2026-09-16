using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using EasyAlarm.Models;

namespace EasyAlarm.Services
{
    /// <summary>
    /// 알람 데이터를 JSON 파일에 저장/불러오기/삭제하는 창고지기.
    ///
    /// 세 겹의 안전장치가 걸려 있습니다.
    ///  ① 자물쇠    — 읽기·쓰기를 한 덩어리로 묶어, 두 곳이 동시에 덮어쓰는 사고를 막습니다.
    ///  ② 통째 교체 — 임시 파일에 다 쓴 뒤 바꿔치기해서, '쓰다 만 반쪽 파일'이 생기지 않게 합니다.
    ///  ③ 손상 복구 — 파일이 깨졌을 때 빈 목록으로 덮어쓰지 않고, 직전 정상본으로 되살립니다.
    ///
    /// 폴더에 생기는 파일 (%appdata%\EasyAlarm):
    ///   alarms.json            — 본 장부
    ///   alarms.bak.json        — 직전 정상본 (교체할 때마다 자동 갱신)
    ///   alarms.corrupt-*.json  — 깨진 파일 격리본 (사고 시에만 생성, 자동 삭제하지 않음)
    /// </summary>
    public class StorageService
    {
        private readonly string _folderPath;
        private readonly string _filePath;
        private readonly string _backupPath;
        private readonly string _tempPath;

        private const int CompletedRetentionDays = 30;

        /// <summary>
        /// 장부 파일 자물쇠.
        /// ⚠️ 반드시 static이어야 합니다. 창고지기는 창마다 따로 만들어지므로
        ///    (위젯·팝업·목록 창·동기화 배달부), 인스턴스마다 걸쇠를 달면
        ///    서로를 막아주지 못해 자물쇠가 아무 소용이 없습니다.
        /// </summary>
        private static readonly object _fileLock = new object();

        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        /// <summary>파일을 읽어본 결과</summary>
        private enum ReadOutcome
        {
            Ok,        // 정상적으로 읽음
            Missing,   // 파일이 아직 없음 (첫 실행 등 — 정상)
            Empty,     // 내용이 비어 있음 (정상으로 간주)
            Corrupt    // 내용이 깨져서 해석 불가 → 복구 절차 필요
        }

        public StorageService()
        {
            _folderPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "EasyAlarm");

            _filePath = Path.Combine(_folderPath, "alarms.json");
            _backupPath = Path.Combine(_folderPath, "alarms.bak.json");
            _tempPath = Path.Combine(_folderPath, "alarms.tmp.json");

            Directory.CreateDirectory(_folderPath);
        }

        // ───────────────────── 읽기 ─────────────────────

        /// <summary>
        /// 서류함에서 모든 알람을 꺼내옵니다.
        /// 본 장부가 깨져 있으면 옆으로 치워 보관한 뒤 직전 정상본으로 되살립니다.
        /// </summary>
        public List<AlarmModel> LoadAlarms()
        {
            lock (_fileLock)
            {
                ReadOutcome outcome = TryRead(_filePath, out List<AlarmModel> alarms);

                if (outcome == ReadOutcome.Ok)
                {
                    return alarms;
                }

                // 파일이 없거나 비어 있는 건 사고가 아니다 (첫 실행 등)
                if (outcome != ReadOutcome.Corrupt)
                {
                    return new List<AlarmModel>();
                }

                // ── 여기부터는 '장부가 깨진' 비상 상황 ──
                LogService.LogError("알람 파일 손상 감지",
                    new InvalidDataException($"해석할 수 없는 장부: {_filePath}"));

                QuarantineCorrupt();

                if (TryRead(_backupPath, out List<AlarmModel> restored) == ReadOutcome.Ok)
                {
                    // 되살린 내용을 본 장부 자리에 즉시 돌려놓는다
                    try
                    {
                        WriteAtomic(restored);
                    }
                    catch (Exception ex)
                    {
                        LogService.LogError("백업본 복구 저장", ex);
                    }

                    return restored;
                }

                // 백업본마저 없거나 깨졌다 → 빈 목록으로 시작하되,
                // 격리해 둔 alarms.corrupt-*.json 이 남아 있으므로 수동 복구 여지는 있다.
                return new List<AlarmModel>();
            }
        }

        /// <summary>파일 하나를 읽어 해석해 봅니다. 예외를 밖으로 던지지 않습니다.</summary>
        private static ReadOutcome TryRead(string path, out List<AlarmModel> result)
        {
            result = new List<AlarmModel>();

            try
            {
                if (!File.Exists(path))
                {
                    return ReadOutcome.Missing;
                }

                string json = File.ReadAllText(path);

                if (string.IsNullOrWhiteSpace(json))
                {
                    return ReadOutcome.Empty;
                }

                List<AlarmModel>? parsed = JsonSerializer.Deserialize<List<AlarmModel>>(json);

                if (parsed == null)
                {
                    return ReadOutcome.Corrupt;
                }

                result = parsed;
                return ReadOutcome.Ok;
            }
            catch (Exception)
            {
                return ReadOutcome.Corrupt;
            }
        }

        /// <summary>
        /// 깨진 장부를 지우지 않고 옆으로 치워 둡니다.
        /// (자동 복구가 실패해도 나중에 사람이 열어볼 수 있게 남겨두는 것이 목적)
        /// </summary>
        private void QuarantineCorrupt()
        {
            try
            {
                string quarantinePath = Path.Combine(
                    _folderPath, $"alarms.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json");

                File.Move(_filePath, quarantinePath, true);
            }
            catch (Exception ex)
            {
                LogService.LogError("손상 파일 격리", ex);
            }
        }

        // ───────────────────── 쓰기 ─────────────────────

        /// <summary>
        /// 알람 목록 전체를 저장합니다.
        /// ⚠️ 저장에 실패하면 예외를 그대로 던집니다 — 조용히 삼키면 사용자가
        ///    '등록됐다'고 착각하게 되므로, 부르는 쪽에서 오류를 표시해야 합니다.
        /// </summary>
        public void SaveAlarms(List<AlarmModel> alarms)
        {
            lock (_fileLock)
            {
                WriteAtomic(alarms);
            }
        }

        /// <summary>
        /// 임시 파일에 전부 쓴 뒤 본 장부와 통째로 바꿔치기합니다.
        /// 원본 위에 덧칠하지 않으므로, 쓰는 도중 멈춰도 본 장부는 멀쩡합니다.
        ///
        /// File.Replace가 '교체'와 '직전본 백업'을 한 번에 처리해 줍니다.
        /// 교체에 실패하면 본 장부는 손대지 않은 상태 그대로 남습니다(= 안전한 실패).
        ///
        /// ※ 한계: 운영체제 쓰기 버퍼까지 강제로 비우지는 않습니다.
        ///    앱이 죽는 상황은 이걸로 충분히 막히고, 전원 차단은 백업본이 받쳐줍니다.
        ///    (반드시 자물쇠를 쥔 상태에서 호출할 것)
        /// </summary>
        private void WriteAtomic(List<AlarmModel> alarms)
        {
            string json = JsonSerializer.Serialize(alarms, _jsonOptions);

            File.WriteAllText(_tempPath, json);

            if (File.Exists(_filePath))
            {
                File.Replace(_tempPath, _filePath, _backupPath);
            }
            else
            {
                File.Move(_tempPath, _filePath);
            }
        }

        // ───────────────────── 개별 작업 ─────────────────────
        // 아래 메서드들은 '읽고 → 고치고 → 쓰기'를 한 덩어리로 처리해야 하므로
        // 전체를 자물쇠로 감쌉니다. (자물쇠는 같은 스레드에서 다시 잠가도 안전합니다)

        /// <summary>새 알람 1개를 추가합니다.</summary>
        public void AddAlarm(AlarmModel alarm)
        {
            lock (_fileLock)
            {
                List<AlarmModel> alarms = LoadAlarms();
                alarms.Add(alarm);
                SaveAlarms(alarms);
            }
        }

        /// <summary>고유번호가 일치하는 알람 1개를 삭제합니다.</summary>
        public void DeleteAlarm(Guid id)
        {
            lock (_fileLock)
            {
                List<AlarmModel> alarms = LoadAlarms();
                alarms.RemoveAll(alarm => alarm.Id == id);
                SaveAlarms(alarms);
            }
        }

        /// <summary>완료된 알람을 전부 파기합니다. (전체삭제 버튼용)</summary>
        public void DeleteAllCompleted()
        {
            lock (_fileLock)
            {
                List<AlarmModel> alarms = LoadAlarms();
                alarms.RemoveAll(alarm => alarm.IsCompleted);
                SaveAlarms(alarms);
            }
        }

        /// <summary>보존 기간(30일)이 지난 완료 알람을 자동 파기합니다.</summary>
        public void CleanupOldCompleted()
        {
            lock (_fileLock)
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

        /// <summary>
        /// 특정 알람의 구글 동기화 결과를 기록합니다.
        /// (동기화는 백그라운드에서 진행되므로, 결과가 나오는 대로 장부에 도장을 찍는 용도)
        /// </summary>
        public void UpdateSyncResult(Guid id, string? googleEventId, SyncState state)
        {
            try
            {
                lock (_fileLock)
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
            }
            catch (Exception ex)
            {
                LogService.LogError("동기화 결과 기록", ex);
            }
        }
    }
}
