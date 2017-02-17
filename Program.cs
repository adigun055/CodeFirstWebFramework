﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.IO;

namespace CodeFirstWebFramework {
	/// <summary>
	/// Class to maintain a dated log file
	/// </summary>

	public class DailyLog : System.Diagnostics.TraceListener {
		DateTime _lastDate = DateTime.MinValue;
		string _logFolder;
		StreamWriter _sw = null;
		bool _autoclose;

		public DailyLog(string logFolder) {
			_logFolder = logFolder;
			_autoclose = false;
			if (!Directory.Exists(_logFolder)) {
				Directory.CreateDirectory(_logFolder);
			}
			System.Diagnostics.Trace.Listeners.Add(this);
		}

		public override void Close() {
			if (_sw != null)
				_sw.Close();
			_sw = null;
		}

		public override void Flush() {
			Close();
		}

		/// <summary>
		/// Purges log files with names dated earlier than the given date
		/// </summary>
		/// <param name="before"></param>
		/// <param name="folder">Folder to look for the log files in</param>
		/// <remarks>Ignores files that don't look like daily log files</remarks>

		public void Purge(DateTime before, string folder) {
			Regex mask = new Regex(@"^\d{4}-\d{2}-\d{2}\.log$", RegexOptions.IgnoreCase);
			foreach (string file in Directory.GetFiles(folder, "*.log")) {
				if (mask.IsMatch(Path.GetFileName(file))) {
					DateTime date = DateTime.ParseExact(Path.GetFileNameWithoutExtension(file), "yyyy-MM-dd", new System.Globalization.CultureInfo("en-GB"));
					if (date < before) File.Delete(file);
				}
			}
		}

		public void Purge(DateTime before) {
			Purge(before, _logFolder);

		}

		/// <summary>
		/// Write exact text given to the file
		/// </summary>

		public override void Write(string text) {
			lock (this) {
				open();
				try {
					_sw.Write(text);
				} finally {
					if (_autoclose) Close();
				}
			}
		}

		/// <summary>
		/// Write exact text given to the file
		/// </summary>
		/// <param name="line">Line to write or null to just flush the file when a new day starts</param>

		public override void WriteLine(string line) {
			lock (this) {
				open();
				try {
					_sw.WriteLine(Utils.Now.ToString("HH:mm:ss") + " " + line);
				} finally {
					if (_autoclose) Close();
				}
			}
		}

		string fileName() {
			_lastDate = Utils.Today;
			return fileName(_lastDate);
		}

		string fileName(DateTime date) {
			return Path.Combine(_logFolder, date.ToString("yyyy-MM-dd") + ".log");
		}

		void open() {
			if (_sw == null || Utils.Today != _lastDate) {
				if (_sw != null) {
					_sw.Close();
				}
				_sw = new StreamWriter(new FileStream(fileName(), FileMode.Append, FileAccess.Write, FileShare.ReadWrite), Encoding.UTF8);
				_sw.AutoFlush = true;
			}
		}

	}

}
