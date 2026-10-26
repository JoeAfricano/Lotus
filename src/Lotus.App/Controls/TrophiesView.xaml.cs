using Lotus.Core.Achievements;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lotus.App.Controls;

// "Conquistas" section of the diary: summary, almost-there, rarest Steam unlocks and every Lotus achievement.
public sealed partial class TrophiesView : UserControl
{
    private AchievementsViewModel? Vm => DataContext as AchievementsViewModel;
    private bool filling;
    public TrophiesView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => { if (Vm is { } vm) { vm.Tabs.CollectionChanged -= OnTabs; vm.Tabs.CollectionChanged += OnTabs; FillTabs(); } };
    }

    private void OnTabs(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => DispatcherQueue.TryEnqueue(FillTabs);

    // Category bar with "unlocked/total" on each item; keeps the current choice.
    private void FillTabs()
    {
        if (Vm is not { } vm || vm.Tabs.Count == 0) return;
        filling = true;
        if (CategoryBar.Items.Count != vm.Tabs.Count)
        {
            CategoryBar.Items.Clear();
            foreach (var t in vm.Tabs) CategoryBar.Items.Add(new SelectorBarItem { Tag = t.Id });
        }
        for (var i = 0; i < vm.Tabs.Count; i++)
        {
            var item = CategoryBar.Items[i];
            item.Text = $"{vm.Tabs[i].Label}  {vm.Tabs[i].CountText}";
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item, vm.Tabs[i].Label);
        }
        CategoryBar.SelectedItem = CategoryBar.Items.FirstOrDefault(i => (string)i.Tag == vm.Category) ?? CategoryBar.Items[0];
        filling = false;
    }

    private void OnCategory(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (filling || Vm is not { } vm || sender.SelectedItem?.Tag is not string id || id == vm.Category) return;
        vm.Category = id; UiSound.Play(UiSound.Cue.Toggle);
    }

    // The medal springs when the section opens.
    public void Celebrate() => Motion.Pop(SummaryIcon);
}
