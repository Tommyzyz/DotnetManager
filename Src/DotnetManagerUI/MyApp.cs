namespace DotnetManagerUI
{
    /// <summary>
    /// The MAUI application object the embedded BlazorWebView needs. It carries no UI of its own.
    /// </summary>
    public partial class MyApp : Microsoft.Maui.Controls.Application
    {
        public MyApp()
        {
            Resources.Add("Primary", Color.Parse("#512BD4"));
        }
    }
}
