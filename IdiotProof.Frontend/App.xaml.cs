namespace IdiotProof.Frontend
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new Window(new MainPage())
            {
                Title = "IdiotProof Strategy Builder",
                MinimumWidth = 1200,
                MinimumHeight = 800,
                Width = 1400,
                Height = 900
            };
        }
    }
}
