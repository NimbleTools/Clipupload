﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Collections;
using System.Net;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Collections.Specialized;
using AddonHelper;
using Renci.SshNet;
using System.Threading;

namespace SFTP
{
  public class SFTP : Addon
  {
    public Settings settings;

    public string sshServer = "";
    public int sshPort = 22;
    public string sshUsername = "";
    public string sshPassword = "";
    public string sshPath = "";
    public string sshHttp = "";

    public string sshError = "";

    public string imageFormat = "PNG";
    public bool useMD5 = false;
    public bool shortMD5 = false;
    public int length = 8;

    public bool jpegCompression = false;
    public int jpegCompressionFilesize = 1000;
    public int jpegCompressionRate = 75;

    public string shortCutDragModifiers = "";
    public string shortCutDragKey = "";
    public string shortCutAnimModifiers = "";
    public string shortCutAnimKey = "";
    public string shortCutPasteModifiers = "";
    public string shortCutPasteKey = "";

    private Bitmap bmpIcon;
    private Bitmap bmpIcon16;

    private SftpClient sshConnection;
    public Action<bool> connectionDoneCallback = null;

    public SFTP()
    {
      Icon icon = new Icon(AddonPath + "/Icon.ico");
      this.bmpIcon = icon.ToBitmap();
      this.bmpIcon16 = new Icon(icon, new Size(16, 16)).ToBitmap();
    }

    public override AddonInfo GetInfo()
    {
      return new AddonInfo() {
        Name = "SFTP",
        Author = "Angelo Geels",
        Icon = bmpIcon16,
        //URL = "http://clipupload.net/",
        URL_Author = "http://angelog.nl/"
      };
    }

    public override void Initialize()
    {
      this.settings = new Settings(AddonPath + "/settings.txt");

      LoadSettings();
    }

    public override void Uninitialize()
    {
      if (sshConnection != null) {
        try {
          sshConnection.Disconnect();
        } catch { }
        sshConnection = null;
      }
    }

    public void MigrateSettings()
    {
    }

    public void LoadSettings()
    {
      MigrateSettings();

      sshServer = settings.GetString("Server");
      sshPort = settings.GetInt("Port");
      sshUsername = settings.GetString("Username");
      sshPassword = base64Decode(settings.GetString("Password"));
      sshPath = settings.GetString("Path");
      sshHttp = settings.GetString("Http");

      imageFormat = settings.GetString("Format");

      useMD5 = settings.GetBool("UseMD5");
      shortMD5 = settings.GetBool("ShortMD5");

      length = settings.GetInt("Length");

      jpegCompression = settings.GetBool("JpegCompression");
      jpegCompressionFilesize = settings.GetInt("JpegCompressionFilesize");
      jpegCompressionRate = settings.GetInt("JpegCompressionRate");

      shortCutDragModifiers = settings.GetString("ShortcutDragModifiers");
      shortCutDragKey = settings.GetString("ShortcutDragKey");
      shortCutAnimModifiers = settings.GetString("ShortcutAnimModifiers");
      shortCutAnimKey = settings.GetString("ShortcutAnimKey");
      shortCutPasteModifiers = settings.GetString("ShortcutPasteModifiers");
      shortCutPasteKey = settings.GetString("ShortcutPasteKey");

      Connect();
    }

    public void Connect()
    {
      if (sshConnection != null) {
        if (sshConnection.IsConnected) {
          try {
            sshConnection.Disconnect();
          } catch { }
        }
        sshConnection = null;
      }

      if (sshServer != "" && sshUsername != "" && sshPassword != "") {
        sshConnection = new SftpClient(sshServer, sshPort, sshUsername, sshPassword);
        // connect in a different thread, we don't want to block the main thread
        new Thread(new ThreadStart(delegate
        {
          bool ok = false;
          try {
            sshConnection.Connect();
            ok = true;
          } catch (Exception ex) {
            Tray.ShowBalloonTip(5000, "SFTP", "Failed to connect to SFTP server: \"" + (ex.InnerException != null ? ex.InnerException.Message : ex.Message) + "\"", ToolTipIcon.Error);
            sshConnection = null;
          }
          if (connectionDoneCallback != null) {
            connectionDoneCallback(ok);
            connectionDoneCallback = null;
          }
        })).Start();
      }
    }

    public override MenuEntry[] Menu()
    {
      List<MenuEntry> ret = new List<MenuEntry>();

      if (sshServer != "" && sshUsername != "" && sshPath != "" && sshHttp != "") {
        ret.Add(new MenuEntry() {
          IsDragItem = true,
          Text = "SFTP",
          Image = this.bmpIcon16,
          Action = new Action(delegate { this.Drag(new Action<DragCallback>(DragCallback)); }),
          ActionSecondary = new Action(delegate { this.Drag(new Action<DragCallback>(DragCallbackSecondary)); }),
          ShortcutModifiers = this.shortCutDragModifiers,
          ShortcutKey = this.shortCutDragKey
        });

        ret.Add(new MenuEntry() {
          IsAnimateItem = true,
          Text = "SFTP",
          Image = this.bmpIcon16,
          Action = new Action(delegate { this.Drag(new Action<DragCallback>(DragCallback), true); }),
          ActionSecondary = new Action(delegate { this.Drag(new Action<DragCallback>(DragCallbackSecondary), true); }),
          ShortcutModifiers = this.shortCutAnimModifiers,
          ShortcutKey = this.shortCutAnimKey
        });

        ret.Add(new MenuEntry() {
          Visible = ClipboardContainsImage || ClipboardContainsFileList || ClipboardContainsText,
          Text = "SFTP",
          Image = this.bmpIcon16,
          Action = new Action(delegate { Upload(false); }),
          ActionSecondary = new Action(delegate { Upload(true); }),
          ShortcutModifiers = this.shortCutPasteModifiers,
          ShortcutKey = this.shortCutPasteKey
        });

        if (Android.AllOK()) {
          AndroidDevice[] devices = Android.ListDevices();
          foreach (AndroidDevice device in devices) {
            string strDeviceSerial = device.SerialNumber;
            ret.Add(new MenuEntry() {
              IsAndroid = true,
              ShowShortInfo = false,
              Text = device.Model,
              SubEntries = new List<MenuEntry>(new MenuEntry[] {
                new MenuEntry() {
                  IsAndroidScreenshotItem = true,
                  Text = "Screenshot",
                  Image = this.bmpIcon16,
                  Action = new Action(delegate { AndroidScreenshot(strDeviceSerial, false); }),
                  ActionSecondary = new Action(delegate { AndroidScreenshot(strDeviceSerial, true); })
                },
                new MenuEntry() {
                  IsAndroidVideoItem = true,
                  Text = "Video",
                  Image = this.bmpIcon16,
                  Action = new Action(delegate { AndroidVideo(strDeviceSerial, false); }),
                  ActionSecondary = new Action(delegate { AndroidVideo(strDeviceSerial, true); })
                }
              })
            });
          }
        }
      }

      return ret.ToArray();
    }

    public override void Settings()
    {
      new FormSettings(this).ShowDialog();
    }

    public override ShellInfo[] ShowShell(string[] files)
    {
      List<ShellInfo> ret = new List<ShellInfo>();

      ret.Add(new ShellInfo() {
        Text = GetShellText(files, "SFTP"),
        Image = this.bmpIcon16,
        Identifier = "up"
      });

      return ret.ToArray();
    }

    public override void ShellCalled(string identifier, string[] files)
    {
      if (identifier == "up") {
        StringCollection arr = new StringCollection();
        arr.AddRange(files);
        UploadFiles(arr, false);
      }
    }

    public void DragCallback(DragCallback callback)
    {
      switch (callback.Type) {
        case DragCallbackType.Image:
          UploadImage(callback.Image, callback.CustomFilename);
          break;

        case DragCallbackType.Animation:
          UploadAnimation(callback.Animation, callback.CustomFilename);
          break;

        case DragCallbackType.Gif:
          UploadAnimation(callback.Animation, callback.CustomFilename);
          break;
      }
    }

    public void DragCallbackSecondary(DragCallback callback)
    {
      callback.CustomFilename = true;
      DragCallback(callback);
    }

    CustomFilenameInfo OpenCFI(string filename)
    {
      return OpenCustomFilenameDialog("Enter a custom filename:", false, "", filename, false);
    }

    public void UploadImage(Image img, bool askCustomFilename)
    {
      MemoryStream ms = new MemoryStream();

      ImageFormat format = ImageFormat.Png;
      string formatStr = imageFormat.ToLower();

      switch (formatStr) {
        case "png": format = ImageFormat.Png; break;
        case "jpg": format = ImageFormat.Jpeg; break;
        case "gif": format = ImageFormat.Gif; break;
      }

      img = this.ImagePipeline(img);

      img.Save(ms, format);

      if (jpegCompression && Control.ModifierKeys != Keys.Shift) {
        if (ms.Length / 1000 > jpegCompressionFilesize) {
          ms.Dispose();
          ms = new MemoryStream();

          // Set up the encoder, codec and params
          System.Drawing.Imaging.Encoder jpegEncoder = System.Drawing.Imaging.Encoder.Compression;
          ImageCodecInfo jpegCodec = this.GetEncoder(ImageFormat.Jpeg);
          EncoderParameters jpegParams = new EncoderParameters();
          jpegParams.Param[0] = new EncoderParameter(jpegEncoder, jpegCompressionRate);

          // Now save it with the new encoder
          img.Save(ms, jpegCodec, jpegParams);

          // And make sure the filename gets set correctly
          formatStr = "jpg";
        }
      }

      bool overwrite = false;
      string filename = this.RandomFilename(this.settings.GetInt("Length"));
      if (this.useMD5) {
        filename = MD5(filename + rnd.Next(1000, 9999).ToString());

        if (this.shortMD5)
          filename = filename.Substring(0, this.length);
      }
      filename += "." + formatStr;

      if (askCustomFilename) {
        CustomFilenameInfo cfi = OpenCFI(filename);
        if (cfi == null) {
          return;
        }
        filename = cfi.UserInput;
        overwrite = cfi.Checkbox;
      }

      Icon defIcon = (Icon)Tray.Icon.Clone();
      Tray.Icon = new Icon(AddonPath + "/Icon.ico", new Size(16, 16));

      bool result = false;
      string failReason = "";

      bool canceled = false;
      try {
        if (!filename.EndsWith("." + formatStr)) {
          filename += "." + formatStr;
        }

        this.Backup(ms.GetBuffer(), filename);
        canceled = !UploadToSFTP(ms, filename, overwrite);

        result = true;
      } catch (Exception ex) { failReason = sshError != "" ? sshError : (ex.InnerException != null ? ex.InnerException.Message : ex.Message); }

      if (!canceled) {
        if (result) {
          Uploaded("Image", sshHttp + filename, img.Width + " x " + img.Height);
        } else {
          Failed(failReason);
        }
      }

      img.Dispose();

      Tray.Icon = defIcon;
    }

    public void UploadAnimation(MemoryStream ms, bool askCustomFilename, string extension = "")
    {
      bool overwrite = false;
      string filename = this.RandomFilename(this.settings.GetInt("Length"));
      if (this.useMD5) {
        filename = MD5(filename + rnd.Next(1000, 9999).ToString());

        if (this.shortMD5)
          filename = filename.Substring(0, this.length);
      }
      filename += ".gif";

      if (askCustomFilename) {
        CustomFilenameInfo cfi = OpenCFI(filename);
        if (cfi == null) {
          return;
        }
        filename = cfi.UserInput;
        overwrite = cfi.Checkbox;
      }

      bool result = false;
      string failReason = "";

      Icon defIcon = (Icon)Tray.Icon.Clone();
      Tray.Icon = new Icon(AddonPath + "/Icon.ico", new Size(16, 16));

      bool canceled = false;
      try {
        if (extension == "") {
          if (!filename.EndsWith(".gif")) {
            filename += ".gif";
          }
        } else {
          filename += "." + extension;
        }

        canceled = !UploadToSFTP(ms, filename, overwrite);

        this.Backup(ms.GetBuffer(), filename);

        result = true;
      } catch (Exception ex) { failReason = sshError != "" ? sshError : (ex.InnerException != null ? ex.InnerException.Message : ex.Message); }

      if (!canceled) {
        if (result) {
          Uploaded("Animation", sshHttp + filename, (ms.Length / 1000) + " kB");
        } else {
          Failed(failReason);
        }
      }

      Tray.Icon = defIcon;
    }

    public void UploadText(string Text, bool askCustomFilename)
    {
      bool overwrite = false;
      string filename = this.RandomFilename(this.settings.GetInt("Length"));
      if (this.useMD5) {
        filename = MD5(filename + rnd.Next(1000, 9999).ToString());

        if (this.shortMD5)
          filename = filename.Substring(0, this.length);
      }
      filename += ".txt";

      if (askCustomFilename) {
        CustomFilenameInfo cfi = OpenCFI(filename);
        if (cfi == null) {
          return;
        }
        filename = cfi.UserInput;
        overwrite = cfi.Checkbox;
      }

      Icon defIcon = (Icon)Tray.Icon.Clone();
      Tray.Icon = new Icon(AddonPath + "/Icon.ico", new Size(16, 16));

      bool result = false;
      string failReason = "";

      byte[] textData = Encoding.UTF8.GetBytes(Text);

      bool canceled = false;
      try {
        canceled = !UploadToSFTP(new MemoryStream(textData), filename, overwrite);

        this.Backup(textData, filename);

        result = true;
      } catch (Exception ex) { failReason = sshError != "" ? sshError : (ex.InnerException != null ? ex.InnerException.Message : ex.Message); }

      if (!canceled) {
        if (result) {
          Uploaded("Text", sshHttp + filename, Text.Length + " characters");
        } else {
          Failed(failReason);
        }
      }

      Tray.Icon = defIcon;
    }

    public void UploadFiles(StringCollection files, bool askCustomFilename)
    {
      Icon defIcon = (Icon)Tray.Icon.Clone();
      Tray.Icon = new Icon(AddonPath + "/Icon.ico", new Size(16, 16));

      bool result = false;
      string failReason = "";
      List<UploadedFile> uploadedFiles = new List<UploadedFile>();

      bool canceled = false;

      try {
        foreach (string file in files) {
          string filename = file.Split('/', '\\').Last();
          bool overwrite = false;

          if (askCustomFilename) {
            CustomFilenameInfo cfi = OpenCFI(filename);
            if (cfi == null) {
              continue;
            }
            filename = cfi.UserInput;
            overwrite = cfi.Checkbox;
          }

          canceled = !UploadToSFTP(new MemoryStream(File.ReadAllBytes(file)), filename, overwrite);
          if (canceled)
            break;

          uploadedFiles.Add(new UploadedFile() {
            URL = sshHttp + Uri.EscapeDataString(filename),
            Info = (new FileInfo(file).Length / 1000) + " kB"
          });
        }

        result = true;
      } catch (Exception ex) { failReason = sshError != "" ? sshError : (ex.InnerException != null ? ex.InnerException.Message : ex.Message); }

      if (!canceled) {
        if (result) {
          UploadedFiles(uploadedFiles);
        } else {
          Failed(failReason);
        }
      }

      Tray.Icon = defIcon;
    }

    public void AndroidScreenshot(string strDeviceSerial, bool askCustomFilename)
    {
      UploadImage(Image.FromFile(Android.PullScreenshot(strDeviceSerial)), askCustomFilename);
    }

    public void AndroidVideo(string strDeviceSerial, bool askCustomFilename)
    {
      FormAndroidRecord recorder = new FormAndroidRecord(strDeviceSerial);
      recorder.Callback = (string strFilename) => {
        StringCollection coll = new StringCollection();

        string filename = this.RandomFilename(this.settings.GetInt("Length"));
        if (this.useMD5) {
          filename = MD5(filename + rnd.Next(1000, 9999).ToString());

          if (this.shortMD5)
            filename = filename.Substring(0, this.length);
        }
        filename = Path.GetTempPath() + filename + ".mp4";
        File.Move(strFilename, filename);

        coll.Add(filename);
        UploadFiles(coll, askCustomFilename);
      };
      recorder.Show();
    }

    public bool UploadToSFTP(MemoryStream ms, string filename, bool overwrite)
    {
      string strPath = sshPath;
      if (!strPath.StartsWith("/")) {
        strPath = "/" + strPath;
      }
      if (!strPath.EndsWith("/")) {
        strPath += "/";
      }

      bool bRet = true;
      bool bWait = true;

      sshError = "";

      if (sshConnection == null || !sshConnection.IsConnected) {
        bool bResume = false;
        bool bOK = false;
        connectionDoneCallback = (bool bSuccess) => {
          bOK = bSuccess;
          bResume = true;
        };
        Connect();
        while (!bResume) System.Threading.Thread.Sleep(1);
        if (!bOK) {
          sshError = "Couldn't connect to server.";
          return false;
        }
      }

      try {
        this.ProgressBar.Start(filename, ms.Length);
        ms.Seek(0, SeekOrigin.Begin);
        IAsyncResult arUpload = null;
        AsyncCallback cbFinished = (IAsyncResult ar) => {
          bRet = true;
          bWait = false;
        };
        Action<ulong> cbProgress = new Action<ulong>((ulong offset) => {
          this.ProgressBar.Set((long)offset);
          if (this.ProgressBar.Canceled) {
            sshConnection.EndUploadFile(arUpload);
            bRet = false;
            bWait = false;
          }
        });
        try {
          arUpload = sshConnection.BeginUploadFile(ms, strPath + filename, overwrite, cbFinished, filename, cbProgress);
        } catch {
          // failed to upload, queue it for a retry after reconnecting
          sshConnection = null;
          connectionDoneCallback = (bool bSuccess) => {
            if (!bSuccess) {
              bRet = false;
              sshError = "Upload failed because couldn't connect to server.";
              bWait = false;
              return;
            }
            try {
              arUpload = sshConnection.BeginUploadFile(ms, strPath + filename, overwrite, cbFinished, filename, cbProgress);
            } catch (Exception ex) {
              bRet = false;
              sshError = "Upload failed twice: " + (ex.InnerException != null ? ex.InnerException.Message : ex.Message);
              bWait = false;
            }
          };
          Connect();
        }
      } catch (Exception ex) {
        bRet = false;
        bWait = false;
        sshError = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
        throw ex;
      }

      while (bWait) System.Threading.Thread.Sleep(1);

      this.ProgressBar.Done();
      return bRet;
    }

    public void Upload(bool askCustomFilename)
    {
      if (Clipboard.ContainsImage())
        UploadImage(Clipboard.GetImage(), askCustomFilename);
      else if (Clipboard.ContainsText())
        UploadText(Clipboard.GetText(), askCustomFilename);
      else if (Clipboard.ContainsFileDropList()) {
        StringCollection files = Clipboard.GetFileDropList();
        if (files.Count == 1 && (files[0].EndsWith(".png") || files[0].EndsWith(".jpg") || files[0].EndsWith(".gif")))
          UploadImage(Image.FromFile(files[0]), askCustomFilename);
        else
          UploadFiles(files, askCustomFilename && files.Count == 1);
      }
    }
  }
}
