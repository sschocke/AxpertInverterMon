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
        _port = new SerialPort(_config.GetValue<string>("port"));

        _port.BaudRate = 2400;
        _port.StopBits = StopBits.One;
        _port.Parity = Parity.None;
        _port.DataBits = 8;
    }

    // public override async Task StartAsync(CancellationToken cancellationToken)
    // {
    //     _port.Open();
    // }
    // public override async Task StopAsync(CancellationToken cancellationToken)
    // {
    //     _port.Close();
    // }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Axpert InverterMon starting...");
        await Task.Yield();
        var mq = new ConnectionFactory();
        mq.HostName = _config.GetValue<string>("MQHost");
        mq.UserName = _config.GetValue<string>("MQUser");
        mq.Password = _config.GetValue<string>("MQPassword");
        mq.VirtualHost = "/";

        using (var mqConn = mq.CreateConnection())
        {
            using (var channel = mqConn.CreateModel())
            {
                channel.ExchangeDeclare("inverter", ExchangeType.Topic, false, false);

                while (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogDebug("Worker running at: {time}", DateTimeOffset.Now);
                    try
                    {
                        var status = new InverterStatus();
                        var success = true;
                        _port.Open();
                        // query(port, "QVFW");
                        var modeBuf = query("QMOD");
                        var matches = Regex.Matches(modeBuf, RegexConstants.QMODregex, RegexOptions.IgnoreCase);
                        if (matches.Count == 1 && matches[0].Groups.Count == 2)
                        {
                            switch (matches[0].Groups[1].Value)
                            {
                                case "B":
                                    _logger.LogDebug("Currently in battery mode");
                                    status.Mode = "Battery";
                                    break;
                                case "L":
                                    _logger.LogDebug("Currently in line mode");
                                    status.Mode = "Line";
                                    break;
                                default:
                                    _logger.LogError("Unknown mode");
                                    success = false;
                                    break;
                            }
                        }

                        var valsBuf = query("QPIGS");
                        matches = Regex.Matches(valsBuf, RegexConstants.QPIGSregex, RegexOptions.IgnoreCase);
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
                                _logger.LogDebug($"Grid Voltage: {gridVoltage}V");
                                status.GridVoltage = gridVoltage;
                                _logger.LogDebug($"Grid Frequency: {gridFrequency}Hz");
                                status.GridFrequency = gridFrequency;
                                _logger.LogDebug($"Output Voltage: {outputVoltage}V");
                                status.OutputVoltage = outputVoltage;
                                _logger.LogDebug($"Output Frequency: {outputFrequency}Hz");
                                status.OutputFrequency = outputFrequency;
                                _logger.LogDebug($"Load: {loadVA}VA");
                                status.LoadVA = loadVA;
                                _logger.LogDebug($"Load: {loadWatt}W");
                                status.LoadWatt = loadWatt;
                                _logger.LogDebug($"Load: {loadPercentage}%");
                                status.LoadPercentage = loadPercentage;
                                _logger.LogDebug($"Bus Voltage: {busVoltage}V");
                                status.BusVoltage = busVoltage;
                                _logger.LogDebug($"Battery Voltage: {batteryVoltage}V");
                                status.BatteryVoltage = batteryVoltage;
                                _logger.LogDebug($"Battery Charge Current: {batteryChargeCurrent}A");
                                status.BatteryChargeCurrent = batteryChargeCurrent;
                                _logger.LogDebug($"Battery Capacity: {batteryCapacity}%");
                                status.BatteryCapacity = batteryCapacity;
                                _logger.LogDebug($"Battery Discharge Current: {batteryDischargeCurrent}A");
                                status.BatteryDischargeCurrent = batteryDischargeCurrent;
                                _logger.LogDebug($"Heatsink Temperature: {heatsinkTemperature}");
                                status.HeatsinkTemperature = heatsinkTemperature;
                                _logger.LogDebug($"PV Input Current: {pvInputCurrent}A");
                                status.PvInputCurrent = pvInputCurrent;
                                _logger.LogDebug($"PV Input Voltage: {pvInputVoltage}V");
                                status.PvInputVoltage = pvInputVoltage;
                                _logger.LogDebug($"SCC Voltage: {sccVoltage}V");
                                status.SccVoltage = sccVoltage;
                                _logger.LogDebug($"Load On: {loadStatusOn}");
                                status.LoadStatusOn = loadStatusOn;
                                _logger.LogDebug($"SCC Charge: {sccChargeOn}");
                                status.SccChargeOn = sccChargeOn;
                                _logger.LogDebug($"AC Charge: {acChargeOn}");
                                status.AcChargeOn = acChargeOn;
                            }
                        }
                        // query("QPIRI");
                        // query("QPIWS");

                        if (success) {
                            var statusStr = JsonSerializer.Serialize(status);
                            var body = Encoding.UTF8.GetBytes(statusStr);
                            var bodySpan = new ReadOnlyMemory<byte>(body);

                            channel.BasicPublish("inverter", "status", null, bodySpan);
                        }
                    }
                    catch (System.Exception)
                    {
                        _logger.LogError("Could not read inverter");
                    }
                    finally
                    {
                        _port.Close();
                    }
                    
                    await Task.Delay(5000, stoppingToken);
                }
            }
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
}
