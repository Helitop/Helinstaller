using System.DirectoryServices.ActiveDirectory;
using System.Text; // ОБЯЗАТЕЛЬНО: нужно для StringBuilder

namespace Helinstaller.Views.Windows
{
    public class OfficeConfiguration
    {
        // --- Свойства (галочки) ---
        public bool InstallProPlus { get; set; } = true;
        public bool InstallVisio { get; set; } = false;
        public bool InstallProject { get; set; } = false;

        // True = Установить, False = Исключить
        public bool IncludeWord { get; set; } = true;
        public bool IncludeExcel { get; set; } = true;
        public bool IncludePowerPoint { get; set; } = true;
        public bool IncludeOutlook { get; set; } = false;
        public bool IncludeAccess { get; set; } = false;

        // --- ТОТ САМЫЙ МЕТОД, КОТОРЫЙ НЕ МОЖЕТ НАЙТИ КОМПИЛЯТОР ---
        public string GenerateXml()
        {
            var sb = new StringBuilder();

            sb.AppendLine("<Configuration>");
            sb.AppendLine("  <Add OfficeClientEdition=\"64\" Channel=\"PerpetualVL2024\">");

            // ProPlus
            sb.AppendLine("    <Product ID=\"ProPlus2024Volume\" PIDKEY=\"XJ2XN-FW8RK-P4HMP-DKDBV-GCVGB\">");
            sb.AppendLine("      <Language ID=\"MatchOS\" />");

            if (!IncludeWord) sb.AppendLine("      <ExcludeApp ID=\"Word\" />");
            if (!IncludeExcel) sb.AppendLine("      <ExcludeApp ID=\"Excel\" />");
            if (!IncludePowerPoint) sb.AppendLine("      <ExcludeApp ID=\"PowerPoint\" />");
            if (!IncludeOutlook) sb.AppendLine("      <ExcludeApp ID=\"Outlook\" />");
            if (!IncludeAccess) sb.AppendLine("      <ExcludeApp ID=\"Access\" />");

            // Стандартные исключения
            sb.AppendLine("      <ExcludeApp ID=\"Lync\" />");
            sb.AppendLine("      <ExcludeApp ID=\"OneDrive\" />");
            sb.AppendLine("      <ExcludeApp ID=\"OneNote\" />");
            sb.AppendLine("      <ExcludeApp ID=\"Publisher\" />");
            sb.AppendLine("      <ExcludeApp ID=\"Teams\" />");
            sb.AppendLine("    </Product>");

            // Visio
            if (InstallVisio)
            {
                sb.AppendLine("    <Product ID=\"VisioPro2024Volume\" PIDKEY=\"B7TN8-FJ8V3-7QYCP-HQPMV-YY89G\">");
                sb.AppendLine("      <Language ID=\"MatchOS\" />");
                sb.AppendLine("      <ExcludeApp ID=\"Lync\" />");
                sb.AppendLine("      <ExcludeApp ID=\"OneDrive\" />");
                sb.AppendLine("      <ExcludeApp ID=\"OneNote\" />");
                sb.AppendLine("      <ExcludeApp ID=\"Outlook\" />");
                sb.AppendLine("      <ExcludeApp ID=\"Publisher\" />");
                sb.AppendLine("    </Product>");
            }

            // Project
            if (InstallProject)
            {
                sb.AppendLine("    <Product ID=\"ProjectPro2024Volume\" PIDKEY=\"F3J96-NB3MX-GTP8Q-9XC4K-Q646R\">");
                sb.AppendLine("      <Language ID=\"MatchOS\" />");
                sb.AppendLine("      <ExcludeApp ID=\"Lync\" />");
                sb.AppendLine("      <ExcludeApp ID=\"OneDrive\" />");
                sb.AppendLine("      <ExcludeApp ID=\"OneNote\" />");
                sb.AppendLine("      <ExcludeApp ID=\"Outlook\" />");
                sb.AppendLine("      <ExcludeApp ID=\"Publisher\" />");
                sb.AppendLine("    </Product>");
            }

            sb.AppendLine("  </Add>");
            sb.AppendLine("  <Property Name=\"SharedComputerLicensing\" Value=\"0\" />");
            sb.AppendLine("  <Property Name=\"FORCEAPPSHUTDOWN\" Value=\"FALSE\" />");
            sb.AppendLine("  <Property Name=\"DeviceBasedLicensing\" Value=\"0\" />");
            sb.AppendLine("  <Property Name=\"SCLCacheOverride\" Value=\"0\" />");
            sb.AppendLine("  <Property Name=\"AUTOACTIVATE\" Value=\"1\" />");
            sb.AppendLine("  <Updates Enabled=\"TRUE\" />");
            sb.AppendLine("  <RemoveMSI />");
            sb.AppendLine("</Configuration>");

            return sb.ToString();
        }
    }
}