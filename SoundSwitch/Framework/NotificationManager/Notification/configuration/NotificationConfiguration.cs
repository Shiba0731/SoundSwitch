﻿/********************************************************************
 * Copyright (C) 2015-2017 Antoine Aflalo
 *
 * This program is free software; you can redistribute it and/or
 * modify it under the terms of the GNU General Public License
 * as published by the Free Software Foundation; either version 2
 * of the License, or (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 ********************************************************************/

using System;
using System.IO;
using System.Windows.Forms;
using SoundSwitch.Framework.Audio;
using SoundSwitch.Framework.Banner.BannerPosition;
using SoundSwitch.Framework.Banner.MicrophoneMute;

namespace SoundSwitch.Framework.NotificationManager.Notification.Configuration;

public class NotificationConfiguration : INotificationConfiguration
{
    public NotifyIcon Icon { get; set; }
    public Stream DefaultSound { get; set; }
    public CachedSound CustomSound { get; set; }
    public BannerPositionEnum BannerPosition { get; set; }
    public TimeSpan Ttl { get; set; }
    public MicrophoneMuteEnum MicrophoneMuteNotification { get; set; }
}