using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Web;
using System.Configuration;
using System.Globalization;
using System.Reflection;
using Mustache;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CodeFirstWebFramework {
	public class Config {
		[JsonIgnore]
		ServerConfig _default;
		public static readonly string EntryModule = System.Reflection.Assembly.GetEntryAssembly().Name();
		public static readonly string DefaultNamespace = "CodeFirstWebFramework";
		public string Database = "SQLite";
		public string Namespace = DefaultNamespace;
		public string ConnectionString = "Data Source=" + Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData).Replace(@"\", "/")
			+ @"/" + EntryModule + "/" + EntryModule + ".db";
		[JsonIgnore]
		public string Filename;
		public int Port = 8080;
		public int SlowQuery = 100;
		public string ServerName = "localhost";
		public string Email = "root@localhost";
		public int SessionExpiryMinutes = 30;
		public List<ServerConfig> Servers = new List<ServerConfig>();
		public bool SessionLogging;
		public int DatabaseLogging;
		public bool PostLogging;
		static public NameValueCollection CommandLineFlags;

		public static Config Default = new Config();

		public void Save(string filename) {
			using (StreamWriter w = new StreamWriter(filename))
				w.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(this, Newtonsoft.Json.Formatting.Indented));
		}

		[JsonIgnore]
		public ServerConfig DefaultServer {
			get {
				lock (this) {
					if (_default == null)
						_default = new ServerConfig() {
							ServerName = ServerName,
							Email = Email,
							Namespace = Namespace
						};
				}
				return _default;
			}
		}

		public ServerConfig SettingsForHost(string host) {
			if (Servers != null) {
				ServerConfig settings = Servers.FirstOrDefault(s => s.Matches(host));
				if (settings != null)
					return settings;
			}
			return DefaultServer;

		}

		static public void Load(string filename) {
			WebServer.Log("Loading config from {0}", filename);
			using (StreamReader s = new StreamReader(filename)) {
				Default = Utils.JsonTo<Config>(s.ReadToEnd());
				Default.Filename = Path.GetFileNameWithoutExtension(filename);
				Default._default = null;
			}
		}

		static public void Load(string[] args) {
			try {
				string configName = Assembly.GetExecutingAssembly().Name() + ".config";
				if (File.Exists(configName))
					Config.Load(configName);
				else
					Config.Default.Save(configName);
				NameValueCollection flags = new NameValueCollection();
				foreach (string arg in args) {
					string value = arg;
					string name = Utils.NextToken(ref value, "=");
					flags[name] = value;
					if (value == "") {
						switch (Path.GetExtension(arg).ToLower()) {
							case ".config":
								configName = arg;
								Config.Load(arg);
								continue;
						}
					}
				}
				Config.CommandLineFlags = flags;
#if false
				// Default to UK culture and time (specify empty culture and/or tz to use machine values)
				if (flags["culture"] != "") {
					CultureInfo c = new CultureInfo(flags["culture"] ?? "en-GB");
					Thread.CurrentThread.CurrentCulture = c;
					CultureInfo.DefaultThreadCurrentCulture = c;
					CultureInfo.DefaultThreadCurrentUICulture = c;
				}
				if (flags["tz"] != "")
					Utils._tz = TimeZoneInfo.FindSystemTimeZoneById(flags["tz"] ?? (windows ? "GMT Standard Time" : "GB"));
#endif
#if DEBUG
				if (flags["now"] != null) {
					DateTime now = Utils.Now;
					DateTime newDate = DateTime.Parse(flags["now"]);
					if (newDate.Date == newDate)
						newDate = newDate.Add(now - now.Date);
					Utils._timeOffset = newDate - now;
				}
#endif
				new DailyLog("Logs." + Config.Default.Port).WriteLine("Started:config=" + configName);
				Utils.Check(!string.IsNullOrEmpty(Config.Default.ConnectionString), "You must specify a ConnectionString in the " + configName + " file");
			} catch (Exception ex) {
				WebServer.Log(ex.ToString());
			}
		}
	}

	public class ServerConfig {
		public string ServerName;
		public string ServerAlias;
		public string Namespace;
		public string Email;
		public string Title;
		public string Database;
		public string ConnectionString;
		[JsonIgnore]
		public Namespace NamespaceDef;

		public FileInfo FileInfo(string filename) {
			FileInfo f;
			if (filename.StartsWith("/"))
				filename = filename.Substring(1);
			f = new FileInfo(Path.Combine(ServerName, filename));
			Utils.Check(f.FullName.StartsWith(new FileInfo(ServerName).FullName), "Illegal file access");
			if (f.Exists)
				return f;
			f = new FileInfo(Path.Combine(Namespace, filename));
			if (f.Exists)
				return f;
			f = new FileInfo(Path.Combine(CodeFirstWebFramework.Config.Default.ServerName, filename));
			if (f.Exists)
				return f;
			return new FileInfo(Path.Combine(CodeFirstWebFramework.Config.DefaultNamespace, filename));
		}

		public bool Matches(string host) {
			if (host == ServerName)
				return true;
			host = host.ToLower();
			if (host == ServerName)
				return true;
			if (!string.IsNullOrWhiteSpace(ServerAlias)) {
				string pattern = "^(" + Regex.Escape(Regex.Replace(ServerAlias, " +", " ")).Replace(@"\ ", "|").Replace(@"\*", ".*").Replace(@"\?", ".") + ")$";
				if (Regex.IsMatch(host, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline))
					return true;
			}
			return false;
		}

		public string LoadFile(string filename) {
			FileInfo f = FileInfo(filename.ToLower());
			if (!f.Exists)
				f = FileInfo(filename); ;
			Utils.Check(f.Exists, "File not found:{0}", filename);
			using (StreamReader s = new StreamReader(f.FullName, AppModule.Encoding)) {
				string text = s.ReadToEnd();
				text = Regex.Replace(text, @"\{\{ *include +(.*) *\}\}", delegate(Match m) {
					return LoadFile(m.Groups[1].Value);
				});
				text = Regex.Replace(text, @"//[\s]*{{([^{}]+)}}[\s]*$", "{{$1}}");
				text = Regex.Replace(text, @"'!{{([^{}]+)}}'", "{{$1}}");
				text = Regex.Replace(text, @"{{{([^{}]+)}}}", "\x1{{$1}}\x2");
				return text;
			}
		}

		public string LoadTemplate(string filename, object obj) {
			try {
				if (Path.GetExtension(filename) == "")
					filename += ".tmpl";
				return TextTemplate(LoadFile(filename), obj);
			} catch (DatabaseException) {
				throw;
			} catch (Exception ex) {
				throw new CheckException(ex, "{0}:{1}", filename, ex.Message);
			}
		}

		public string TextTemplate(string text, object obj) {
			FormatCompiler compiler = new FormatCompiler();
			compiler.RemoveNewLines = false;
			Generator generator = compiler.Compile(text);
			string result = generator.Render(obj);
			result = Regex.Replace(result, "\x1(.*?)\x2", delegate(Match m) {
				return HttpUtility.HtmlEncode(m.Groups[1].Value).Replace("\n", "\n<br />");
			}, RegexOptions.Singleline);
			return result;
		}

	}

	[Table]
	public class Settings : JsonObject {
		public int DbVersion;
	}

}