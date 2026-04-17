namespace XboxGamingBarHelper.OnScreenDisplay
{
    internal interface IOnScreenDisplayProvider
    {
        bool IsInUsed { get; set; }

        // Sets the OSD level.
        void SetLevel(int level);
    }
}
