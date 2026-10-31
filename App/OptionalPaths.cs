namespace SkullgirlsModManager.Desktop;

public partial class MainForm
{
    private void ShowOptionalPaths(Mod mod)
    {
        if (busy) return;
        if (mod.Info.OptionalPaths.Count == 0) { MessageBox.Show(this, "This mod has no optional components.", "Mod options"); return; }
        using var dialog = CreateOptionalPathsDialog(mod);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var choices = dialog.Controls[0].Controls.OfType<CheckedListBox>().Single();
            mod.SetOptionalSelections(mod.Info.OptionalPaths.Select((o, i) => (o.Id, Enabled: choices.GetItemChecked(i))).ToDictionary(o => o.Id, o => o.Enabled));
            Dirty();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not save mod options"); }
    }
    private Form CreateOptionalPathsDialog(Mod mod)
    {
        var dialog = new Form { Text = mod.Info.Name + " — Optional components", ClientSize = new Size(520, 340),
            StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), RowCount = 3, ColumnCount = 1 };
        layout.RowStyles.Add(new(SizeType.Absolute, 48)); layout.RowStyles.Add(new(SizeType.Percent, 100)); layout.RowStyles.Add(new(SizeType.Absolute, 48));
        layout.Controls.Add(new Label { Text = "Choose optional components. Preview and Apply to update the game.", Dock = DockStyle.Fill }, 0, 0);
        var choices = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
        var selected = mod.OptionalSelections();
        foreach (var option in mod.Info.OptionalPaths) choices.Items.Add(option.Name, selected[option.Id]);
        layout.Controls.Add(choices, 0, 1);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var save = new Button { Text = "Save", DialogResult = DialogResult.OK, AutoSize = true };
        actions.Controls.Add(save); actions.Controls.Add(new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true });
        layout.Controls.Add(actions, 0, 2); dialog.Controls.Add(layout); dialog.AcceptButton = save;
        Theme.Apply(dialog);
        return dialog;
    }
    public void SmokeOptionalPaths()
    {
        var mod = library.Mods()[0];
        string path = mod.Replacements(true).SelectMany(a => a.Value.Keys.Select(k => a.Key + "/" + k)).First();
        mod.Info.OptionalPaths = [new() { Id = "smoke", Name = "Optional replacement", Paths = [path] }];
        mod.Info.Write(Path.Combine(mod.Folder, Mod.MODINFO_NAME));
        ReloadMods(mod.Folder);
        var optionRow = modGrid.Rows.Cast<DataGridViewRow>().Single(r => ((Mod)r.Tag!).Folder == mod.Folder);
        if (optionRow.Cells["options"] is not DataGridViewButtonCell || !optionRow.Cells["options"].Displayed)
            throw new InvalidOperationException("Optional mod has no visible row button.");
        if (modGrid.Rows.Cast<DataGridViewRow>().Where(r => ((Mod)r.Tag!).Info.OptionalPaths.Count == 0).Any(r => r.Cells["options"] is DataGridViewButtonCell))
            throw new InvalidOperationException("Mod without options has an options button.");
        using (var listShot = new Bitmap(Width, Height))
        {
            DrawToBitmap(listShot, new Rectangle(Point.Empty, Size));
            listShot.Save(Path.Combine(Program.ProjectRoot, "optional-mod-row-preview.png"));
        }
        using var dialog = CreateOptionalPathsDialog(mod);
        dialog.Show(this); dialog.PerformLayout();
        var choices = dialog.Controls[0].Controls.OfType<CheckedListBox>().Single();
        if (!choices.Visible || choices.Items.Count != 1 || choices.GetItemChecked(0)) throw new InvalidOperationException("Optional component toggle is not visible or defaults on.");
        choices.SetItemChecked(0, true);
        if (!choices.GetItemChecked(0)) throw new InvalidOperationException("Optional component cannot be checked.");
        using var shot = new Bitmap(dialog.Width, dialog.Height);
        dialog.DrawToBitmap(shot, new Rectangle(Point.Empty, dialog.Size));
        shot.Save(Path.Combine(Program.ProjectRoot, "optional-components-preview.png"));
        dialog.Close();
    }
}
