using System.Windows;

namespace EasyAlarm.Views
{
    /// <summary>
    /// 앱 톤에 맞춘 예/아니오 확인창. 윈도우 기본 경고창(MessageBox)을 대신합니다.
    ///
    /// 기본 경고창은 글자 굵기·색·배경을 바꿀 수 없어서, "무엇을 지우는지"를
    /// 굵게 보여줄 방법이 없었습니다. 이 창은 [제목] · [본문] · [강조 줄] 세 칸으로 나눠
    /// 할일명이 한눈에 들어오게 합니다.
    ///
    /// 사용법: <c>if (ConfirmDialogWindow.Ask(this, "알람 삭제", "삭제할까요?", "치과 예약")) { ... }</c>
    /// Enter = 예 / Esc = 아니오 (버튼의 IsDefault·IsCancel이 알아서 처리합니다)
    /// </summary>
    public partial class ConfirmDialogWindow : Window
    {
        private ConfirmDialogWindow(Window? owner, string title, string message, string highlight)
        {
            InitializeComponent();

            TitleText.Text = title;
            MessageText.Text = message;

            // 강조할 내용이 없으면 그 칸을 통째로 숨긴다 (빈 상자가 남지 않도록)
            if (string.IsNullOrWhiteSpace(highlight))
            {
                HighlightBox.Visibility = Visibility.Collapsed;
            }
            else
            {
                HighlightText.Text = highlight;
            }

            if (owner != null && owner.IsLoaded)
            {
                Owner = owner;

                // 부모가 '항상 위' 창(위젯·팝업)일 때만 따라서 위로 올라간다.
                // 목록 창처럼 평범한 창에서 부를 때는 다른 프로그램을 가리지 않는다.
                Topmost = owner.Topmost;
            }
            else
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        }

        /// <summary>
        /// 확인창을 띄우고 사용자가 [예]를 눌렀는지 돌려줍니다.
        /// </summary>
        /// <param name="owner">이 창을 띄운 창 (그 창 중앙에 표시됩니다)</param>
        /// <param name="title">제목 줄</param>
        /// <param name="message">설명 문장</param>
        /// <param name="highlight">굵게 강조할 내용(할일명 등). 비우면 그 칸이 사라집니다.</param>
        public static bool Ask(Window? owner, string title, string message, string highlight = "")
        {
            ConfirmDialogWindow dialog = new ConfirmDialogWindow(owner, title, message, highlight);
            return dialog.ShowDialog() == true;
        }

        private void YesButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;   // 창은 DialogResult가 정해지는 순간 자동으로 닫힙니다
        }
    }
}
