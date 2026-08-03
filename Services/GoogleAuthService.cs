using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;

namespace EasyAlarm.Services
{
    /// <summary>
    /// 구글 계정 로그인(출입증 발급) 담당.
    /// 최초 1회 브라우저로 동의를 받고, 이후에는 저장된 토큰으로 조용히 재사용합니다.
    /// 토큰은 %appdata%\EasyAlarm\google-token\ 에 보관 (소스코드·저장소에 남기지 않음).
    /// </summary>
    public class GoogleAuthService
    {
        // 요청 권한: 캘린더 보기 + 수정 (일정 생성·삭제에 필요)
        private static readonly string[] Scopes = { CalendarService.Scope.Calendar };

        // ⚠️ 구글 서버에 보내는 발신자 표기(HTTP 헤더)에 쓰이므로 반드시 영문/숫자만!
        // 한글을 넣으면 FormatException이 발생합니다.
        private const string AppName = "ShifShifAlarm";

        /// <summary>인증 파일 위치: 실행 파일과 같은 폴더</summary>
        private static string SecretPath =>
            Path.Combine(AppContext.BaseDirectory, "client_secret.json");

        /// <summary>토큰 보관 폴더</summary>
        private static string TokenFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "EasyAlarm",
            "google-token");

        /// <summary>인증 파일이 준비되어 있는지</summary>
        public bool HasSecretFile => File.Exists(SecretPath);

        /// <summary>이미 로그인된 상태인지 (토큰이 보관되어 있는지)</summary>
        public bool IsConnected =>
            Directory.Exists(TokenFolder) && Directory.EnumerateFiles(TokenFolder).Any();

        /// <summary>
        /// 구글 캘린더와 대화할 수 있는 통로를 만듭니다.
        /// 토큰이 없으면 브라우저가 열려 로그인·동의를 요청합니다(최초 1회).
        /// 반환된 객체는 사용 후 Dispose해야 합니다.
        /// </summary>
        public async Task<CalendarService> CreateCalendarServiceAsync(CancellationToken ct = default)
        {
            if (!HasSecretFile)
            {
                throw new FileNotFoundException(
                    "구글 인증 파일(client_secret.json)을 찾을 수 없습니다.", SecretPath);
            }

            UserCredential credential;

            using (FileStream stream = new FileStream(SecretPath, FileMode.Open, FileAccess.Read))
            {
                credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                    GoogleClientSecrets.FromStream(stream).Secrets,
                    Scopes,
                    "user",
                    ct,
                    new FileDataStore(TokenFolder, true));
            }

            return new CalendarService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = AppName
            });
        }

        /// <summary>로그인 해제: 보관된 토큰을 삭제합니다.</summary>
        public void Disconnect()
        {
            try
            {
                if (Directory.Exists(TokenFolder))
                {
                    Directory.Delete(TokenFolder, true);
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("구글 로그인 해제", ex);
            }
        }
    }
}