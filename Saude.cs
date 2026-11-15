// Saúde do PC: estado dos discos (o próprio Windows avalia), temperatura e horas de uso dos discos,
// espaço das unidades e há quanto tempo o PC está ligado.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;

namespace Folego
{
    class Disco
    {
        public string Nome, Tipo;
        public int Saude;          // 0 saudável, 1 atenção, 2 com problema, -1 desconhecido
        public int Temp = -1;      // °C
        public long Horas = -1;    // horas ligado
        public int Desgaste = -1;  // % (SSD)
        public long GB;
    }

    class Unidade { public string Letra, Nome; public double Livre, Total; }

    static class Saude
    {
        [DllImport("kernel32.dll")] static extern ulong GetTickCount64();

        public static TimeSpan Ligado() { return TimeSpan.FromMilliseconds(GetTickCount64()); }

        public static List<Disco> Discos()
        {
            List<Disco> r = new List<Disco>();
            try
            {
                ManagementScope sc = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(sc, new ObjectQuery("SELECT * FROM MSFT_PhysicalDisk")))
                    foreach (ManagementBaseObject o in s.Get())
                    {
                        int media = Convert.ToInt32(o["MediaType"] ?? 0), bus = Convert.ToInt32(o["BusType"] ?? 0);
                        Disco d = new Disco
                        {
                            Nome = Convert.ToString(o["FriendlyName"]).Trim(),
                            Tipo = bus == 7 ? "Externo (USB)" : media == 4 ? "SSD" : media == 3 ? "HD" : "Disco",
                            Saude = o["HealthStatus"] == null ? -1 : Convert.ToInt32(o["HealthStatus"]),
                            GB = (long)(Convert.ToUInt64(o["Size"] ?? 0UL) / 1000000000UL),
                        };
                        ManagementBaseObject c = null;
                        try // os contadores vêm associados a cada disco (a consulta direta volta vazia)
                        {
                            foreach (ManagementBaseObject rc in ((ManagementObject)o).GetRelated("MSFT_StorageReliabilityCounter")) { c = rc; break; }
                        }
                        catch { }
                        if (c != null)
                        {
                            if (c["Temperature"] != null && Convert.ToInt32(c["Temperature"]) > 0) d.Temp = Convert.ToInt32(c["Temperature"]);
                            if (c["PowerOnHours"] != null) d.Horas = Convert.ToInt64(c["PowerOnHours"]);
                            if (c["Wear"] != null && d.Tipo == "SSD") d.Desgaste = Convert.ToInt32(c["Wear"]);
                        }
                        r.Add(d);
                    }
            }
            catch { }
            return r.OrderBy(d => d.Tipo == "SSD" ? 0 : d.Tipo == "HD" ? 1 : 2).ToList();
        }

        public static List<Unidade> Unidades()
        {
            List<Unidade> r = new List<Unidade>();
            foreach (DriveInfo d in DriveInfo.GetDrives())
                try
                {
                    if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                    r.Add(new Unidade { Letra = d.Name.Substring(0, 2), Nome = d.VolumeLabel, Livre = d.AvailableFreeSpace / 1073741824.0, Total = d.TotalSize / 1073741824.0 });
                }
                catch { }
            return r;
        }
    }
}
