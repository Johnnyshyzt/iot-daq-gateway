namespace IotDaq.Persistence;

public sealed partial class GatewayPersistence
{
    public InstalledLicenseRow? ReadInstalledLicense()
    {
        lock (_gate)
        {
            using var db = CreateContext();
            return db.InstalledLicense.FirstOrDefault(row => row.Id == 1);
        }
    }

    public void SaveInstalledLicense(string document, string customer, string edition, string importedBy, long unixMs)
    {
        lock (_gate)
        {
            using var db = CreateContext();
            var row = db.InstalledLicense.FirstOrDefault(item => item.Id == 1);
            if (row is null)
            {
                row = new InstalledLicenseRow { Id = 1 };
                db.InstalledLicense.Add(row);
            }

            row.DocumentText = document;
            row.Customer = customer;
            row.Edition = edition;
            row.ImportedBy = importedBy;
            row.ImportedUnixMs = unixMs;
            db.SaveChanges();
        }
    }

    public void ClearInstalledLicense()
    {
        lock (_gate)
        {
            using var db = CreateContext();
            var row = db.InstalledLicense.FirstOrDefault(item => item.Id == 1);
            if (row is null)
            {
                return;
            }

            db.InstalledLicense.Remove(row);
            db.SaveChanges();
        }
    }
}
