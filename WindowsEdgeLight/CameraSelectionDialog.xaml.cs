using System.Windows;

namespace WindowsEdgeLight;

public partial class CameraSelectionDialog : Window
{
    public int SelectedIndex { get; private set; } = -1;

    public CameraSelectionDialog(string[] cameraNames)
    {
        InitializeComponent();
        
        foreach (var name in cameraNames)
        {
            CameraList.Items.Add(name);
        }
        
        if (CameraList.Items.Count > 0)
        {
            CameraList.SelectedIndex = 0;
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedIndex = CameraList.SelectedIndex;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
