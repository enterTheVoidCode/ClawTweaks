namespace XboxGamingBarHelper.OnScreenDisplay
{
    internal interface IOnScreenDisplayProvider
    {
        bool IsInUsed { get; set; }

        bool IsInstalled { get; }

        // Sets the OSD level.
        void SetLevel(int level);
    }
}
