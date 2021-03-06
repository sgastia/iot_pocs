﻿using Gastia.IoT.POCs.Web.CmdBackgroundTask.Interfaces.DeviceInterfaces;
using Gastia.IoT.POCs.Web.CmdBackgroundTask.Interfaces.Entities;
using Gastia.IoT.POCs.Web.CmdBackgroundTask.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;

namespace Gastia.IoT.POCs.Web.CmdBackgroundTask.Interfaces.HttpInterface
{
    internal class WebApiHelper
    {
        private const string URL_TAKE_SNAPSHOT = "/api/devices/camera/snapshot";
        private const string URL_INITIALIZE_CAMERA = "/api/devices/camera/initialize";
        private const string URL_START_VIDEO_RECORDING = "/api/devices/camera/startvideorecording";
        private const string URL_STOP_VIDEO_RECORDING = "/api/devices/camera/stopvideorecording";
        private const string URL_LIVE_VIDEO = "/api/devices/camera/livevideo";

        private readonly Webcam _webcam = new Webcam();

        internal async Task<string> Execute(string requestUri)
        {
            return await ExecuteCamera(requestUri);
        }

        private async Task<string> ExecuteCamera(string requestUri)
        { 
            if (requestUri == URL_INITIALIZE_CAMERA)
            {
                JsonMessage<string> msg = new JsonMessage<string>();
                msg.Ok = true;
                msg.Content = await _webcam.InitializeCamera();
                return msg.Stringify();
            }
            else if (requestUri == URL_TAKE_SNAPSHOT)
            {
                StorageFolder folder = await Windows.ApplicationModel.Package.Current.InstalledLocation.GetFolderAsync(NavConstants.TEMP_FOLDER);
                string name = await _webcam.TakePhoto(folder);
                string webaddress = WebServer.GetServerWebAddress();
                return "{\"photoPath\":\"http://" + webaddress + "/" + NavConstants.TEMP_FOLDER + "/" + name + "\"}";
            }
            else if (requestUri == URL_START_VIDEO_RECORDING)
            {
                StorageFolder folder = await Windows.ApplicationModel.Package.Current.InstalledLocation.GetFolderAsync(NavConstants.TEMP_FOLDER);
                StorageFile file = await folder.CreateFileAsync(Webcam.VIDEO_FILE_NAME, CreationCollisionOption.GenerateUniqueName);
                string message = await _webcam.RecordVideo(file);
                return "{\"message\":\""+message+"\",\"videoPath\":\""+file.Name+"\"}";
            }
            else if (requestUri == URL_STOP_VIDEO_RECORDING)
            {
                string name = await _webcam.RecordVideo(null);
                string webaddress = WebServer.GetServerWebAddress();
                string path = "http://" + webaddress + "/" + NavConstants.TEMP_FOLDER + "/" + name;
                return "{\"message\":\"Video is saved.\",\"videoPath\":\""+path+"\"}";
            }
            else if (requestUri == URL_LIVE_VIDEO)
            {
                JsonMessage<object> error = new JsonMessage<object>();
                error.Ok = false;
                error.ErrorMessage = $"Method {URL_LIVE_VIDEO} not implemented yet";
                return error.Stringify();
            }
            else
            {
                JsonMessage<object> error = new JsonMessage<object>();
                error.Ok = false;
                error.ErrorMessage = $"Method {requestUri} not implemented yet";
                return error.Stringify();
            }

        }    
    }


}
