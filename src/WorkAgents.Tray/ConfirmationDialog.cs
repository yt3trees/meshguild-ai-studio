namespace WorkAgents.Tray;

/// <summary>
/// 進行中Runがある状態で「更新」「終了」が選ばれた際の確認ダイアログ
/// (contracts/tray-menu-contract.md「確認ダイアログ契約」、Clarifications Q1)。
/// 既定ボタンをキャンセル側にして、誤操作でRunを失う事故を防ぐ。
/// </summary>
public static class ConfirmationDialog
{
    public static bool ConfirmActiveRunInterruption(string actionLabel)
    {
        var message = $"実行中のRunが中断される可能性があります。{actionLabel}を続行しますか？";
        var result = MessageBox.Show(
            message,
            "WorkAgents",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        return result == DialogResult.OK;
    }
}
