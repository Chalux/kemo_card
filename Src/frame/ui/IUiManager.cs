using System.Threading;
using System.Threading.Tasks;

namespace KemoCard.Frame.Ui;

public interface IUiManager
{
	Task OpenDlgAsync<TPayload>(string id, TPayload payload, CancellationToken cancellationToken = default);

	Task OpenPopupAsync<TPayload>(string id, TPayload payload, PopupReopenBehavior behavior, bool maskClickClosesPopup = true, CancellationToken cancellationToken = default);

	void CloseTopPopup();

	void CloseDlg();
}
