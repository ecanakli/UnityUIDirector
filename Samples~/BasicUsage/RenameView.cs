using Ecanakli.UIDirector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ecanakli.UIDirector.Samples.BasicUsage
{
    /// <summary>A rename popup written without the base class, for projects whose views already inherit from something else.</summary>
    public sealed class RenameView : MonoBehaviour, IView<RenameArgs, string>, IViewBack, IServiceReceiver
    {
        [SerializeField] private TMP_InputField _input;
        [SerializeField] private Button _save;
        [SerializeField] private Button _cancel;
        [SerializeField] private TMP_Text _hint;

        private ViewContext<string> _context;
        private IItemLibrary _library;
        private bool _edited;
        private bool _discardHintShown;

        public void Receive(AppServices services)
        {
            _library = services.Library;
        }

        public void OnOpen(RenameArgs args, ViewContext<string> context)
        {
            _context = context;

            // Decline is the right call inside OnOpen: nothing is shown and the caller sees Declined.
            if (!_library.CanRename(args.ItemId))
            {
                context.Decline("This item cannot be renamed.");
                return;
            }

            // A reused instance must not carry state from the previous open.
            _edited = false;
            _discardHintShown = false;
            _hint.text = string.Empty;
            _input.SetTextWithoutNotify(args.CurrentTitle);

            context.Open.Bind(_input.onValueChanged, MarkEdited);
            context.Open.Bind(_save.onClick, Save);
            context.Open.Bind(_cancel.onClick, Cancel);
        }

        public bool OnBack()
        {
            if (!_edited || _discardHintShown)
                return false;

            _discardHintShown = true;
            _hint.text = "Press back again to discard the change.";
            return true;
        }

        private void MarkEdited(string text)
        {
            _edited = true;
        }

        private void Save()
        {
            var title = _input.text.Trim();
            if (title.Length == 0)
            {
                _hint.text = "Enter a name first.";
                return;
            }

            _context.Complete(title);
        }

        private void Cancel()
        {
            _context.Close();
        }
    }
}
