namespace HelloWorld
{
    public partial class MainWindow
    {
        public MainWindow()
        {
            this.InitializeComponent();

#if NETFRAMEWORK
            MultiAppDomainFixture.StartIfRequested(this);
#endif
        }
    }
}
