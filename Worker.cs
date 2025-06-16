using System.IO.Ports;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using RabbitMQ.Client;

namespace InverterMon;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly SerialPort _port;
    private readonly IConfiguration _config;
    private readonly UInt16[] crc_ta = {
        0x0000,0x1021,0x2042,0x3063,0x4084,0x50a5,0x60c6,0x70e7,
        0x8108,0x9129,0xa14a,0xb16b,0xc18c,0xd1ad,0xe1ce,0xf1ef
    };


    public Worker(ILogger<Worker> logger, IConfiguration configuration)
    {
        _logger = logger;
        _config = configuration;
        _port = new SerialPort(_config.GetValue<string>("port"))
        {
            BaudRate = 2400,
            StopBits = StopBits.One,
            Parity = Parity.None,
            DataBits = 8,
            ReadTimeout = 30 * 1000,
            WriteTimeout = 30 * 1000,
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Axpert InverterMon starting...");
        await Task.Yield();

        var mq = new ConnectionFactory
        {
            HostName = _config.GetValue<string>("MQHost"),
            UserName = _config.GetValue<string>("MQUser"),
            Password = _config.GetValue<string>("MQPassword"),
            VirtualHost = "/"
        };

        using var mqConn = mq.CreateConnection();
        using var channel = mqConn.CreateModel();

        channel.ExchangeDeclare("inverter", ExchangeType.Topic, false, false);

        InverterStatus status = new InverterStatus();
        InverterStatus? prevStatus = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogDebug("Worker running at: {time}", DateTimeOffset.Now);
            try
            {
                _port.Open();

                var success = QueryMode(status);
                success &= QueryVals(status);

                if (!success)
                {
                    _logger.LogError("Could not read inverter status");
                    if (_port.IsOpen) _port.Close();
                    await Task.Delay(30000, stoppingToken);
                    continue;
                }

                if (success && prevStatus == null)
                {
                    prevStatus = status;
                }

                if (success)
                {
                    var statusStr = JsonSerializer.Serialize(status);
                    var body = Encoding.UTF8.GetBytes(statusStr);
                    var bodySpan = new ReadOnlyMemory<byte>(body);

                    channel.BasicPublish("inverter", "status", null, bodySpan);
                }

                if (success)
                {
                    if (prevStatus != null && prevStatus.Mode != status.Mode)
                    {
                        _logger.LogInformation("Inverter Status changed from {PreviousMode} to {CurrentMode}", prevStatus.Mode, status.Mode);
                    }

                    prevStatus = status;
                }
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Could not read inverter - {ExceptionMessage}", ex.Message);
            }
            finally
            {
                if (_port.IsOpen) _port.Close();
            }

            await Task.Delay(5000, stoppingToken);
        }
    }
    UInt16 CalcCRC(byte[] buffer)
    {
        UInt16 crc = 0;
        var len = buffer.Length;

        foreach (var b in buffer)
        {
            var da = ((byte)(crc >> 8)) >> 4;
            crc <<= 4;
            crc ^= crc_ta[da ^ (b >> 4)];
            da = ((byte)(crc >> 8)) >> 4;
            crc <<= 4;
            crc ^= crc_ta[da ^ (b & 0x0F)];
        }

        var crcLow = (byte)crc;
        var crcHigh = (byte)(crc >> 8);
        if (crcLow == 0x28 || crcLow == 0x0d || crcLow == 0x0a) crcLow++;
        if (crcHigh == 0x28 || crcHigh == 0x0d || crcHigh == 0x0a) crcHigh++;

        crc = (UInt16)(crcHigh << 8);
        crc += crcLow;

        return crc;
    }

    string query(string cmd)
    {
        var cmdBytes = ASCIIEncoding.ASCII.GetBytes(cmd);
        var crc = CalcCRC(cmdBytes);

        var buf = new byte[cmdBytes.Length + 3];
        Array.Copy(cmdBytes, buf, cmdBytes.Length);
        buf[cmdBytes.Length] = ((byte)(crc >> 8));
        buf[cmdBytes.Length + 1] = ((byte)(crc & 0xff));
        buf[cmdBytes.Length + 2] = 0x0d;

        _port.Write(buf, 0, buf.Length);
        var buffer = new byte[1024];
        var pos = 0;
        while (true)
        {
            try
            {
                var read = _port.Read(buffer, pos, buffer.Length - pos);
                if (read > 0)
                {
                    pos += read;
                }
                else
                {
                    Thread.Sleep(5);
                }
                if (buffer.Any(b => b == 0x0d)) { break; }
            }
            catch (TimeoutException)
            {
                break;
            }
        }
        if (buffer.Any(b => b == 0x0d))
        {
            var result = ASCIIEncoding.ASCII.GetString(buffer, 0, pos - 3);
            _logger.LogDebug($"{cmd} Result (byte={pos}): {result}");

            return result;
        }
        return string.Empty;
    }

    private bool QueryMode(InverterStatus status)
    {
        var modeBuf = query("QMOD");
        var matches = Regex.Matches(modeBuf, RegexConstants.QMODregex, RegexOptions.IgnoreCase);
        if (matches.Count == 1 && matches[0].Groups.Count == 2)
        {
            switch (matches[0].Groups[1].Value)
            {
                case "B":
                    _logger.LogDebug("Currently in battery mode");
                    status.Mode = "Battery";
                    return true;
                case "L":
                    _logger.LogDebug("Currently in line mode");
                    status.Mode = "Line";
                    return true;
                default:
                    _logger.LogError("Unknown mode");
                    break;
            }
        }

        return false;
    }

    private bool QueryVals(InverterStatus status)
    {
        var success = true;
        var valsBuf = query("QPIGS");
        var matches = Regex.Matches(valsBuf, RegexConstants.QPIGSregex, RegexOptions.IgnoreCase);
        if (matches.Count == 1 && matches[0].Groups.Count == 25)
        {
            success &= double.TryParse(matches[0].Groups[1].Value, out var gridVoltage);
            success &= double.TryParse(matches[0].Groups[2].Value, out var gridFrequency);
            success &= double.TryParse(matches[0].Groups[3].Value, out var outputVoltage);
            success &= double.TryParse(matches[0].Groups[4].Value, out var outputFrequency);
            success &= int.TryParse(matches[0].Groups[5].Value, out var loadVA);
            success &= int.TryParse(matches[0].Groups[6].Value, out var loadWatt);
            success &= int.TryParse(matches[0].Groups[7].Value, out var loadPercentage);
            success &= int.TryParse(matches[0].Groups[8].Value, out var busVoltage);
            success &= double.TryParse(matches[0].Groups[9].Value, out var batteryVoltage);
            success &= int.TryParse(matches[0].Groups[10].Value, out var batteryChargeCurrent);
            success &= int.TryParse(matches[0].Groups[11].Value, out var batteryCapacity);
            success &= int.TryParse(matches[0].Groups[12].Value, out var heatsinkTemperature);
            success &= int.TryParse(matches[0].Groups[13].Value, out var pvInputCurrent);
            success &= double.TryParse(matches[0].Groups[14].Value, out var pvInputVoltage);
            success &= double.TryParse(matches[0].Groups[15].Value, out var sccVoltage);
            success &= int.TryParse(matches[0].Groups[16].Value, out var batteryDischargeCurrent);
            var loadStatusOn = matches[0].Groups[20].Value == "1";
            var sccChargeOn = matches[0].Groups[23].Value == "1";
            var acChargeOn = matches[0].Groups[24].Value == "1";
            if (success)
            {
                _logger.LogDebug("Grid Voltage: {GridVoltage}V", gridVoltage);
                status.GridVoltage = gridVoltage;
                _logger.LogDebug("Grid Frequency: {GridFrequency}Hz", gridFrequency);
                status.GridFrequency = gridFrequency;
                _logger.LogDebug("Output Voltage: {OutputVoltage}V", outputVoltage);
                status.OutputVoltage = outputVoltage;
                _logger.LogDebug("Output Frequency: {OutputFrequency}Hz", outputFrequency);
                status.OutputFrequency = outputFrequency;
                _logger.LogDebug("Load: {LoadVA}VA", loadVA);
                status.LoadVA = loadVA;
                _logger.LogDebug("Load: {LoadWatt}W", loadWatt);
                status.LoadWatt = loadWatt;
                _logger.LogDebug("Load: {LoadPercentage}%", loadPercentage);
                status.LoadPercentage = loadPercentage;
                _logger.LogDebug("Bus Voltage: {BusVoltage}V", busVoltage);
                status.BusVoltage = busVoltage;
                _logger.LogDebug("Battery Voltage: {BatteryVoltage}V", batteryVoltage);
                status.BatteryVoltage = batteryVoltage;
                _logger.LogDebug("Battery Charge Current: {BatteryChargeCurrent}A", batteryChargeCurrent);
                status.BatteryChargeCurrent = batteryChargeCurrent;
                _logger.LogDebug("Battery Capacity: {BatteryCapacity}%", batteryCapacity);
                status.BatteryCapacity = batteryCapacity;
                _logger.LogDebug("Battery Discharge Current: {BatteryDischargeCurrent}A", batteryDischargeCurrent);
                status.BatteryDischargeCurrent = batteryDischargeCurrent;
                _logger.LogDebug("Heatsink Temperature: {HeatsinkTemperature}", heatsinkTemperature);
                status.HeatsinkTemperature = heatsinkTemperature;
                _logger.LogDebug("PV Input Current: {PvInputCurrent}A", pvInputCurrent);
                status.PvInputCurrent = pvInputCurrent;
                _logger.LogDebug("PV Input Voltage: {PvInputVoltage}V", pvInputVoltage);
                status.PvInputVoltage = pvInputVoltage;
                _logger.LogDebug("SCC Voltage: {SccVoltage}V", sccVoltage);
                status.SccVoltage = sccVoltage;
                _logger.LogDebug("Load On: {LoadStatusOn}", loadStatusOn);
                status.LoadStatusOn = loadStatusOn;
                _logger.LogDebug("SCC Charge: {SccChargeOn}", sccChargeOn);
                status.SccChargeOn = sccChargeOn;
                _logger.LogDebug("AC Charge: {AcChargeOn}", acChargeOn);
                status.AcChargeOn = acChargeOn;

                return true;
            }
        }

        return false;
    }
}
