using System;
using System.IO;
using Renci.SshNet;
using UFiber.Configurator;
using System.CommandLine;
using System.CommandLine.Invocation;

var rootCommand = new RootCommand("Apply configuration changes to UFiber devices")
{
    new Option<string>(
        "--host",
        getDefaultValue: () => "192.168.1.1",
        "IP or hostname of the target UFiber device."),
    new Option<string>(
        "--user",
        getDefaultValue: () => "ubnt",
        "SSH user name."),
    new Option<string>(
        "--pw",
        getDefaultValue: () => "ubnt",
        "SSH password."),
    new Option<int>(
        "--port",
        getDefaultValue: () => 22,
        "SSH port of the target UFiber device."),
    new Option<string>(
        "--restore",
        "Restore a previous (or modified!) version of the firmware.")
};

SshClient GetSSHClient(string userName, string password, string host, int port = 22)
{
    var client = new SshClient(host, port, userName, password);
    client.Connect();
    return client;
}

ScpClient GetSCPClient(string userName, string password, string host, int port = 22)
{
    var client = new ScpClient(host, port, userName, password);
    client.Connect();
    return client;
}

rootCommand.Handler = CommandHandler
    .Create<string, string, string, int, string>(
        (host, user, pw, port, restore) =>
        {
            var fwToRestore = restore; 
            if (string.IsNullOrWhiteSpace(host))
            {
                Console.Error.WriteLine("Host is a required parameter and can't be empty.");
                Environment.ExitCode = -1;
                return;
            }

            SshClient ssh = default!;
            ScpClient scp = default!;

            try
            {
                // Connect to SSH and SCP
                ssh = GetSSHClient(user, pw, host, port);
                scp = GetSCPClient(user, pw, host, port);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unable to connect to the target UFiber device. Please check the connection parameters and try again. Error: {ex.Message}");
                Environment.ExitCode = -1;
                return;
            }

            var imgName = $"fw-{DateTime.UtcNow.ToString("ddMMyyyy-hhmmss")}.bin";

            // Dump the image file
            var cmd = ssh.RunCommand($"cat /dev/mtdblock9 > /tmp/{imgName}");
            if (cmd.ExitStatus != 0)
            {
                Console.Error.WriteLine($"Failute to dump the image file. Error: {cmd.Error}");
                Environment.ExitCode = cmd.ExitStatus;
                return;
            }

            const string localDumps = "./dumps";

            if (!Directory.Exists(localDumps))
            {
                Directory.CreateDirectory(localDumps);
            }

            // Download the dump
            try
            {
                scp.Download($"/tmp/{imgName}", new DirectoryInfo(localDumps));
                ssh.RunCommand($"rm /tmp/{imgName}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failure downloading original image file from the UFiber device. Error: {ex.Message}.");
                Environment.ExitCode = -1;
                return;
            }

            if (!string.IsNullOrWhiteSpace(fwToRestore))
            {
                const string targetFileToRestore = "/tmp/flash.bin";
                Console.WriteLine("Uploading original (or modified) file to the target UFiber device...");
                try
                {
                    scp.Upload(new FileInfo(fwToRestore), targetFileToRestore);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Failure uploading original image file to the UFiber device. Error: {ex.Message}.");
                    Environment.ExitCode = -1;
                    return;
                }
                Console.WriteLine("Uploaded!");
                Console.WriteLine("### Applying original (or modified) file on the target UFiber device...");
                cmd = ssh.RunCommand($"dd if={targetFileToRestore} of=/dev/mtdblock9 && rm {targetFileToRestore}");
                if (cmd.ExitStatus != 0)
                {
                    Console.Error.WriteLine($"Failure to apply original (or modified) image file. Error: {cmd.Error}");
                    Environment.ExitCode = cmd.ExitStatus;
                    return;
                }
                Console.WriteLine("### Applied patch! Please reboot your UFiber device to load the new image.");
                return;
            } 
        });

return rootCommand.Invoke(args);












