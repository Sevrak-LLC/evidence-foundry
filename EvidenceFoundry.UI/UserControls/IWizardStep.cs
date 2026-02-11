using EvidenceFoundry.Models;

namespace EvidenceFoundry.UserControls;

public interface IWizardStep
{
    string StepTitle { get; }
    bool CanMoveNext { get; }
    bool CanMoveBack { get; }
    string NextButtonText { get; }

    event EventHandler? StateChanged;

    void BindState(WizardState state);
    Task OnEnterStepAsync();
    Task OnLeaveStepAsync();
    Task<bool> ValidateStepAsync();
}
