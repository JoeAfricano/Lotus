using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Lotus.Core.Diary;

// "Onde eu parei?" inside the diary editor: a small note saved on its own (Salvar / Editar / Limpar), so it never
// touches the score, hours, status or feats of the entry. Works for games not saved in the diary yet, too.
public sealed partial class DiaryEditorViewModel
{
    public ResumeNotes? Resume { get; set; }
    [ObservableProperty] private string resumeDraft = "";
    [ObservableProperty] private bool resumeEditing;
    private string resumeStatus = "";
    public int ResumeLimit => ResumeNotes.Limit;
    public bool ResumeAvailable => Resume is not null && entry.Key.Length > 0;
    public ResumeNote? ResumeNote => ResumeAvailable ? Resume!.Get(entry.Key) : null;
    public bool HasResume => ResumeNote is not null;
    public bool ShowResumeText => HasResume && !ResumeEditing;
    public bool ShowResumeEmpty => !HasResume && !ResumeEditing;
    public string ResumeText => ResumeNote?.Text ?? "";
    public string ResumeUpdatedText => ResumeNote is { } n ? Text.Format("RnUpdated", n.Updated.LocalDateTime.ToString("g", CultureInfo.GetCultureInfo(Text.Language))) : "";
    // The note was written right after the latest recorded session of this game.
    public bool ResumeFromLastSession => ResumeNote is { SessionStart: { } s } && entry.Sessions.Count > 0 && Math.Abs((entry.Sessions[^1].Start - s.LocalDateTime).TotalMinutes) < 1;
    public string ResumeStatus => resumeStatus;
    private bool ResumeDirty => ResumeEditing && ResumeDraft.Trim() != ResumeText;

    private void LoadResume()
    {
        ResumeEditing = false; ResumeDraft = ""; resumeStatus = "";
        NotifyResume();
    }
    public void EditResume() { ResumeDraft = ResumeText; ResumeEditing = true; resumeStatus = ""; NotifyResume(); }
    public void CancelResume() { ResumeEditing = false; ResumeDraft = ""; NotifyResume(); }
    public bool SaveResume()
    {
        if (!ResumeAvailable) return false;
        var ok = Resume!.Set(entry.Key, ResumeDraft);
        resumeStatus = Text[ok ? "RnSaved" : "RnSaveFailed"];
        if (ok) ResumeEditing = false;
        NotifyResume();
        return ok;
    }
    public bool ClearResume()
    {
        if (!ResumeAvailable) return false;
        var ok = Resume!.Clear(entry.Key);
        resumeStatus = ok ? Text["RnCleared"] : Text["RnSaveFailed"];
        if (ok) { ResumeEditing = false; ResumeDraft = ""; }
        NotifyResume();
        return ok;
    }
    private void NotifyResume()
    {
        foreach (var n in new[] { nameof(ResumeAvailable), nameof(ResumeNote), nameof(HasResume), nameof(ShowResumeText), nameof(ShowResumeEmpty), nameof(ResumeText),
            nameof(ResumeUpdatedText), nameof(ResumeFromLastSession), nameof(ResumeStatus) })
            OnPropertyChanged(n);
    }
    partial void OnResumeEditingChanged(bool value) { OnPropertyChanged(nameof(ShowResumeText)); OnPropertyChanged(nameof(ShowResumeEmpty)); }
}
