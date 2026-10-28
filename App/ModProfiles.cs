using System.Drawing;
using SkullgirlsModManager;

namespace SkullgirlsModManager.Desktop;

public partial class MainForm
{
    private void ShowProfiles()
    {
        try { using var dialog = CreateProfilesDialog(); dialog.ShowDialog(this); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Mod profiles", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private Form CreateProfilesDialog()
    {
        var dialog = new Form { Text = "Named mod profiles", ClientSize = new Size(650, 230), StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(16) };
        layout.RowStyles.Add(new(SizeType.Absolute, 66)); layout.RowStyles.Add(new(SizeType.Absolute, 40));
        layout.RowStyles.Add(new(SizeType.Absolute, 48)); layout.RowStyles.Add(new(SizeType.Percent, 100));
        layout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "Save enabled mods and load order by name. Profiles share the mod library.\r\nPreview a profile before switching the library selection. Then use Preview changes and Apply to update the game." }, 0, 0);
        var picker = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Saved mod profiles" };
        void Reload(string? id = null)
        {
            picker.Items.Clear(); picker.Items.AddRange(library.Profiles().Cast<object>().ToArray());
            int index = picker.Items.Cast<ModProfile>().ToList().FindIndex(p => p.Id == id);
            if (picker.Items.Count > 0) picker.SelectedIndex = index >= 0 ? index : 0;
        }
        Reload(); layout.Controls.Add(picker, 0, 1);
        var actions = Toolbar();
        actions.Controls.Add(Button("Save current…", () =>
        {
            try
            {
                using var naming = new Form { Text = "Save mod profile", ClientSize = new Size(470, 160), StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false };
                var fields = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(16) };
                fields.RowStyles.Add(new(SizeType.Absolute, 30)); fields.RowStyles.Add(new(SizeType.Absolute, 38)); fields.RowStyles.Add(new(SizeType.Percent, 100));
                fields.Controls.Add(new Label { Text = "Name this enabled selection and load order:", Dock = DockStyle.Fill }, 0, 0);
                var name = new TextBox { Dock = DockStyle.Top, MaxLength = 80, AccessibleName = "Profile name" }; fields.Controls.Add(name, 0, 1);
                var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
                var save = new Button { Text = "Save profile", AutoSize = true, DialogResult = DialogResult.OK };
                buttons.Controls.Add(save); buttons.Controls.Add(new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel });
                fields.Controls.Add(buttons, 0, 2); naming.Controls.Add(fields); naming.AcceptButton = save; Theme.Apply(naming);
                if (naming.ShowDialog(dialog) != DialogResult.OK) return;
                bool overwrite = library.Profiles().Any(p => p.Name.Equals(name.Text.Trim(), StringComparison.OrdinalIgnoreCase));
                if (overwrite && MessageBox.Show(dialog, $"Replace the saved selection and order in '{name.Text.Trim()}'?", "Replace mod profile", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
                var saved = library.SaveProfile(name.Text, overwrite); Reload(saved.Id);
                AppendLog($"Saved mod profile '{saved.Name}'. Game installation unchanged.");
            }
            catch (Exception ex) { MessageBox.Show(dialog, ex.Message, "Could not save profile", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }));
        var preview = Button("Preview switch…", async () =>
        {
            if (picker.SelectedItem is not ModProfile profile) return;
            await Run(async () =>
            {
                var reviewed = await Task.Run(() => library.ReviewProfile(profile.Id));
                using var review = CreateProfileReview(reviewed);
                if (review.ShowDialog(dialog) != DialogResult.OK) return;
                string? selected = Selected?.Folder;
                await Task.Run(() => library.SwitchProfile(reviewed));
                ReloadMods(selected); Dirty();
                status.Text = $"Loaded '{reviewed.Profile.Name}'. Preview changes, then Apply to update the game.";
                AppendLog($"Loaded mod profile '{reviewed.Profile.Name}'. Missing entries: {reviewed.Rows.Count(r => r.Folder == null)}. No game files changed.");
                dialog.Close();
            });
        });
        preview.Enabled = picker.SelectedItem != null; picker.SelectedIndexChanged += (_, _) => preview.Enabled = picker.SelectedItem != null;
        actions.Controls.Add(preview); actions.Controls.Add(Button("Close", dialog.Close)); layout.Controls.Add(actions, 0, 2);
        layout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "Switching changes the library selection only. Installation baselines and recovery history stay with their installation." }, 0, 3);
        dialog.Controls.Add(layout); Theme.Apply(dialog); return dialog;
    }

    private Form CreateProfileReview(ProfileReview reviewed)
    {
        var dialog = new Form { Text = "Review profile: " + reviewed.Profile.Name, ClientSize = new Size(920, 540), StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(16) };
        layout.RowStyles.Add(new(SizeType.Absolute, 74)); layout.RowStyles.Add(new(SizeType.Percent, 100)); layout.RowStyles.Add(new(SizeType.Absolute, 48));
        int missing = reviewed.Rows.Count(r => r.Folder == null), changed = reviewed.Rows.Count(r => r.Note.Contains("changed"));
        layout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = $"'{reviewed.Profile.Name}': {reviewed.Rows.Count(r => r.Folder != null && r.Enabled)} available mods will be enabled. {missing} missing entries; {changed} changed identities / versions.\r\nMissing entries are omitted. Mods absent from this profile move to the end and are disabled.\r\nSwitching changes the library selection only. Preview changes and Apply afterward to install it." }, 0, 0);
        var grid = Grid(); grid.ReadOnly = true;
        Column(grid, "order", "Order", 7); Column(grid, "enabled", "Enabled", 8); Column(grid, "mod", "Mod", 24);
        Column(grid, "saved", "Saved", 11); Column(grid, "current", "Current", 11); Column(grid, "note", "Review", 39);
        grid.Columns["saved"]!.HeaderCell.ToolTipText = "Package version saved in the profile";
        grid.Columns["current"]!.HeaderCell.ToolTipText = "Current package version in the shared library";
        grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True; grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        foreach (var row in reviewed.Rows)
        {
            int i = grid.Rows.Add(row.Order?.ToString() ?? "—", row.Folder == null ? "Missing" : row.Enabled ? "Yes" : "No", row.Name, row.SavedVersion, row.CurrentVersion, row.Note);
            if (row.Note.Length > 0) grid.Rows[i].DefaultCellStyle.BackColor = Theme.Burgundy;
        }
        layout.Controls.Add(grid, 0, 1);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        actions.Controls.Add(new Button { Text = "Switch library selection", AutoSize = true, DialogResult = DialogResult.OK });
        actions.Controls.Add(new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel });
        layout.Controls.Add(actions, 0, 2); dialog.Controls.Add(layout); Theme.Apply(dialog); return dialog;
    }

    public async Task SmokeProfiles()
    {
        // SmokeReview leaves two enabled library entries in a known order.
        var initial = library.Mods();
        var saved = library.SaveProfile("Music profile");
        initial[0].SetEnabled(false); library.SetOrder(initial.AsEnumerable().Reverse());
        ReloadMods(initial[0].Folder);
        var reviewed = library.ReviewProfile(saved.Id);
        using (var managerDialog = CreateProfilesDialog())
        {
            managerDialog.Show(this);
            if (!managerDialog.Controls.Cast<Control>().SelectMany(c => c.Controls.Cast<Control>()).OfType<ComboBox>().Single().Items.Cast<ModProfile>().Any(p => p.Id == saved.Id))
                throw new InvalidOperationException("Saved profile is absent from the picker.");
            managerDialog.Close();
        }
        using (var review = CreateProfileReview(reviewed))
        {
            review.Show(this); review.PerformLayout();
            var grid = review.Controls.Cast<Control>().SelectMany(c => c.Controls.Cast<Control>()).OfType<DataGridView>().Single();
            if (grid.Rows.Count != initial.Count || grid.DisplayedRowCount(false) < 2 || !grid.Visible)
                throw new InvalidOperationException("Profile switch review is not visible.");
            using var shot = new Bitmap(review.Width, review.Height); review.DrawToBitmap(shot, new Rectangle(Point.Empty, review.Size));
            shot.Save(Path.Combine(Program.ProjectRoot, "profiles-preview.png")); review.Close();
        }
        library.SwitchProfile(reviewed); ReloadMods();
        if (!library.Mods().Select(m => m.Folder).SequenceEqual(initial.Select(m => m.Folder)) || library.Mods().Any(m => !m.Enabled))
            throw new InvalidOperationException("Profile switch failed to restore selection and order.");
        await Preview();
        File.AppendAllText(Path.Combine(Program.ProjectRoot, "ui-smoke-results.txt"), "PASS named profile picker, switch review, shared-library enabled state and order restoration without applying mods.\n");
    }
}
