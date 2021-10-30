namespace InverterMon;

public class InverterStatus
{
    public string Mode { get; set; } = "Unknown";
    public double GridVoltage { get; set; }
    public double GridFrequency { get; set; }
    public double OutputVoltage { get; set; }
    public double OutputFrequency { get; set; }
    public double LoadVA { get; set; }
    public double LoadWatt { get; set; }
    public double LoadPercentage { get; set; }
    public double BusVoltage { get; set; }
    public double BatteryVoltage { get; set; }
    public double BatteryChargeCurrent { get; set; }
    public double BatteryCapacity { get; set; }
    public double BatteryDischargeCurrent { get; set; }
    public double HeatsinkTemperature { get; set; }
    public double PvInputCurrent { get; set; }
    public double PvInputVoltage { get; set; }
    public double SccVoltage { get; set; }
    public bool LoadStatusOn { get; set; }
    public bool SccChargeOn { get; set; }
    public bool AcChargeOn { get; set; }
}