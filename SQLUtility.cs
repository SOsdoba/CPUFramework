using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace CPUFramework
{
    public class SQLUtility
    {
        private static string ConnectionString = "";

        public static void SetConnectionString(string connstring, bool tryopen,string userid = "", string password = "")
        {
            ConnectionString = connstring;
            if(userid != "")
            {
                SqlConnectionStringBuilder b = new();
                b.ConnectionString = ConnectionString;
                b.UserID = userid;
                b.Password = password;
                ConnectionString = b.ConnectionString;
            }
            if(tryopen)
            {
                using(SqlConnection conn = new SqlConnection(ConnectionString))
                {
                    conn.Open();
                }
            }
        }
        public static SqlCommand GetSQLCommand(string sprocname)
        {
            SqlCommand cmd;
            using (SqlConnection conn = new(SQLUtility.ConnectionString))
            {
                cmd = new(sprocname, conn);
                cmd.CommandType = CommandType.StoredProcedure;

                conn.Open();
                SqlCommandBuilder.DeriveParameters(cmd);
            }
            return cmd;
        }
        public static DataTable GetDataTable(SqlCommand cmd)
        {
            return DoExecuteSQL(cmd, true);
        }

        public static void SaveDataTable(DataTable dt, string sprocname)
        {
            var rows = dt.Select("", "", DataViewRowState.Added | DataViewRowState.ModifiedCurrent);
            foreach(DataRow r in rows)
            {
                SaveDataRow(r, sprocname, false);
            }
            dt.AcceptChanges();
        }

        public static void SaveDataRow(DataRow row, string sprocname, bool acceptchanges = true)
        {
            SqlCommand cmd = GetSQLCommand(sprocname);
            foreach(DataColumn col in row.Table.Columns)
            {
                string paramname = $"@{col.ColumnName}";
                if(cmd.Parameters.Contains(paramname))
                {
                    cmd.Parameters[paramname].Value = row[col.ColumnName];
                }
            }
            DoExecuteSQL(cmd, false);
           
            foreach(SqlParameter p in cmd.Parameters)
            {
                if(p.Direction == ParameterDirection.InputOutput)
                {
                    string colname = p.ParameterName.Substring(1);
                    if(row.Table.Columns.Contains(colname))
                    {
                        row[colname] = p.Value;
                    }
                }
            }
            if(acceptchanges == true)
            {
                row.Table.AcceptChanges();
            }
        }

        private static DataTable DoExecuteSQL(SqlCommand cmd, bool loadtable)
        {
            DataTable dt = new();
            using (SqlConnection conn = new(SQLUtility.ConnectionString))
            {
                conn.Open();
                cmd.Connection = conn;
                Debug.Print(GetSQL(cmd));
                try
                {
                    SqlDataReader dr = cmd.ExecuteReader();
                    CheckReturnValue(cmd);
                    if(loadtable == true)
                    {
                        dt.Load(dr);
                    }
                }
                catch(SqlException ex)
                {
                    string msg = ParseConstraintMsg(ex.Message);
                    throw new Exception(msg);
                }
                catch(InvalidCastException ex)
                {
                    throw new Exception(cmd.CommandText + ": " + ex.Message,ex);
                }  
            }
            SetAllColumnProperties(dt);
            return dt;
        }

        private static void CheckReturnValue(SqlCommand cmd)
        {
            int returnvalue = 0;
            string msg = "";
            if (cmd.CommandType == CommandType.StoredProcedure)
            {
                foreach (SqlParameter p in cmd.Parameters)
                {
                    if (p.Direction == ParameterDirection.ReturnValue)
                    {
                        if (p.Value != null)
                        {
                            returnvalue = (int)p.Value;
                        }
                    }
                    else if (p.ParameterName.ToLower() == "@message")
                    {
                        if (p.Value != null)
                        {
                            msg = p.Value.ToString();
                        }
                    }
                }
                if (returnvalue == 1)
                {
                    if (msg == "")
                    {
                        msg = $"{cmd.CommandText} did not do action that was requested.";
                    }
                    throw new Exception(msg);
                }
            }
        }

        public static DataTable GetDataTable(string sqlstatement)
        {
            return DoExecuteSQL(new SqlCommand(sqlstatement), true);
            //Debug.Print(sqlstatement);
            //DataTable dt = new();
            //SqlConnection conn = new();
            //conn.ConnectionString = ConnectionString;
            //conn.Open();
            
            //var cmd = new SqlCommand();
            //cmd.Connection = conn;
            //cmd.CommandText = sqlstatement;
            //var dr = cmd.ExecuteReader();
            //dt.Load(dr);

            //SetAllColumnsAllowNull(dt);
            
            //return dt;
        }
        public static void ExecuteSQL(SqlCommand cmd)
        {
            DoExecuteSQL(cmd, false);
        }

        public static void ExecuteSQL(string sqlstatement)
        {
            GetDataTable(sqlstatement);
        }

        public static void SetParamValue(SqlCommand cmd, string paramname, object value)
        {
            try
            {
                cmd.Parameters[paramname].Value = value;
            }
            catch (Exception ex)
            {
                throw new Exception(cmd.CommandText + ": " + ex.Message,ex);
            } 
        }

        private static string ParseConstraintMsg(string msg)
        {   
            string origmsg = msg;
            string prefix = "ck_";
            string msgend = "";
            string notnullprefix = "Cannot insert the value NULL into column ";
            if(msg.Contains(prefix) == false)
            {
                if(msg.Contains("u_"))
                {
                    prefix = "u_";
                    msgend = " must be unique.";
                }
                if(msg.Contains("f_"))
                {
                    prefix = "f_";
                }
                else if (msg.Contains("fk_"))
                {
                    prefix = "fk_";
                }
                else if (msg.Contains(notnullprefix))
                {
                    int index = msg.IndexOf(' ');
                    index = msg.IndexOf(' ', index + 1);
                    prefix = msg.Substring(42, index);
                    msgend = " cannot be blank.";

                    return prefix + msgend;
                }
            }

            if (msg.Contains(prefix))
            {
                msg = msg.Replace("\"", "'");
                int pos = msg.IndexOf(prefix) + prefix.Length;
                msg = msg.Substring(pos);
                pos = msg.IndexOf("'");
                if (pos == -1)
                {
                    msg = origmsg;
                }
                else
                {
                    msg = msg.Substring(0, pos);
                    msg = msg.Replace("_", " ");
                    msg = msg + msgend;

                    if (prefix == "f_" || prefix == "fk_")
                    {
                        var words = msg.Split(" ");
                        if(words.Length > 1)
                        {
                            msg = $"Cannot delete {words[0]} because it has a related {words[1]} record.";
                        }
                    }
                }
            }
            return msg;
        }

        public static int GetFirstColumnFirstRowValue(string sql)
        {
            int n = 0;
            DataTable dt = GetDataTable(sql);
            if(dt.Rows.Count > 0 && dt.Columns.Count > 0)
            {
                if (dt.Rows[0][0] != DBNull.Value)
                {
                    int.TryParse(dt.Rows[0][0].ToString(), out n);
                }
            }
            return n;
        }

        private static void SetAllColumnProperties(DataTable dt)
        {
            foreach(DataColumn c in dt.Columns)
            {
                c.AllowDBNull = true;
                c.AutoIncrement = false;
            }
        }

        public static int GetValueFromFirstRowAsInt(DataTable dt, string columnname)
        {
            int value = 0;
            if (dt.Rows.Count > 0)
            {
                DataRow r = dt.Rows[0];
                if (r[columnname] != null && r[columnname] is int)
                {
                    value = (int)r[columnname];
                }
            }
            return value;
        }

        public static string GetValueFromFirstRowAsString(DataTable dt, string columnname)
        {
            string value = "";
            if(dt.Rows.Count > 0)
            {
                DataRow r = dt.Rows[0];
                if (r[columnname] != null && r[columnname] is string)
                {
                    value = (string)r[columnname];
                }
            }
            return value;
        }

        public static bool TableHasChanges(DataTable dt)
        {
            bool b = false;
            if(dt.GetChanges() != null)
            {
                b = true;
            }
            return b;
        }

        public static string GetSQL(SqlCommand cmd)
        {
            string val = "";
#if DEBUG
            StringBuilder sb = new StringBuilder();


            if (cmd.Connection != null)
            {
                //sb.AppendLine($"--{cmd.Connection.ConnectionString}");
                sb.AppendLine($"--{cmd.Connection.DataSource}");
                sb.AppendLine($"use {cmd.Connection.Database}");
                sb.AppendLine("go");
            }

            if(cmd.CommandType == CommandType.StoredProcedure)
            {
                sb.AppendLine($"exec {cmd.CommandText}");
                int paramcount = cmd.Parameters.Count - 1;
                int paramnum = 0;
                string comma = ",";

                foreach(SqlParameter p in cmd.Parameters)
                {
                    if (p.Direction != ParameterDirection.ReturnValue)
                    {
                        if (paramnum == paramcount)
                        {
                            comma = "";
                        }
                        sb.AppendLine($"{p.ParameterName} = {(p.Value == null ? "null" : p.Value.ToString())}{comma}");
                    }
                    paramnum++;
                }
            }
            else
            {
                sb.AppendLine(cmd.CommandText);
            }
            val = sb.ToString();
#endif
            return val;
        }

        public static void DebugPrintDataTable(DataTable dt)
        {
            foreach(DataRow r in dt.Rows) 
            { 
                foreach(DataColumn c in dt.Columns)
                {
                    Debug.Print(c.ColumnName + " = " + r[c.ColumnName].ToString());
                }
            }
        }
    }
}
