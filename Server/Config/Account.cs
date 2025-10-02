using System.Xml.Serialization;
using CentrED.Utility;

namespace CentrED.Server.Config;

public class MonthlyActivity
{
    [XmlAttribute] public int Year { get; set; }
    [XmlAttribute] public int Month { get; set; }
    [XmlAttribute] public int ActiveMinutes { get; set; }
}

public class Account
{
    public Account() : this("")
    {
    }

    public Account
    (
        string accountName,
        string password = "",
        AccessLevel accessLevel = AccessLevel.None,
        List<string>? regions = null
    )
    {
        Name = accountName;
        PasswordHash = Crypto.Md5Hash(password);
        AccessLevel = accessLevel;
        LastPos = new LastPos();
        Regions = regions ?? new List<string>();
        LastLogon = DateTime.MinValue;
    }

    [XmlElement] public string Name { get; set; }
    [XmlElement] public string PasswordHash { get; set; }
    [XmlElement] public AccessLevel AccessLevel { get; set; }
    [XmlElement] public LastPos LastPos { get; set; }
    [XmlArray] [XmlArrayItem("Region")] public List<string> Regions { get; set; }

    [XmlElement] public DateTime LastLogon { get; set; }
    [XmlArray] [XmlArrayItem("Month")] public List<MonthlyActivity> ActivityHistory { get; set; } = new();

    public override string ToString()
    {
        return $"{nameof(Name)}: {Name}, " + $"{nameof(PasswordHash)}: [redacted], " +
               $"{nameof(AccessLevel)}: {AccessLevel}, " + $"{nameof(LastPos)}: {LastPos}, " +
               $"{nameof(Regions)}: {String.Join(",", Regions)}";
    }

    public void UpdatePassword(string password)
    {
        PasswordHash = Crypto.Md5Hash(password);
    }

    public bool CheckPassword(string password)
    {
        return PasswordHash.Equals(Crypto.Md5Hash(password), StringComparison.InvariantCultureIgnoreCase);
    }

    public void AddActivityMinutes(int year, int month, int minutes)
    {
        var activity = ActivityHistory.FirstOrDefault(a => a.Year == year && a.Month == month);
        if (activity == null)
        {
            activity = new MonthlyActivity { Year = year, Month = month, ActiveMinutes = 0 };
            ActivityHistory.Add(activity);

            // Keep only the last 12 months
            if (ActivityHistory.Count > 12)
            {
                // Remove the oldest entry
                var oldest = ActivityHistory.OrderBy(a => a.Year).ThenBy(a => a.Month).First();
                ActivityHistory.Remove(oldest);
            }
        }

        activity.ActiveMinutes += minutes;
    }

    public int GetActivityMinutes(int year, int month)
    {
        var activity = ActivityHistory.FirstOrDefault(a => a.Year == year && a.Month == month);
        return activity?.ActiveMinutes ?? 0;
    }
}