using System.ComponentModel.DataAnnotations;

namespace AutoTf.YubiKeyInteractor;

public class AddKeyBody
{
    public AddKeyBody(string serialNumber, string secret)
    {
        SerialNumber = serialNumber;
        Secret = secret;
    }

    [Required]
    public string SerialNumber { get; set; }

    [Required]
    public string Secret { get; set; }
}