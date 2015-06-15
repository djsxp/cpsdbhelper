﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Xml.Linq;

namespace CpsDbHelper
{
    public abstract class DbHelper<T> where T : DbHelper<T>
    {
        protected readonly string Text;
        protected CommandType CommandType = CommandType.StoredProcedure;
        protected SqlParameter ReturnValue;
        private readonly string _connectionString;
        private readonly IDictionary<string, SqlParameter> _outParameters = new Dictionary<string, SqlParameter>();
        protected readonly IDictionary<string, SqlParameter> Parameters = new Dictionary<string, SqlParameter>();

        private Func<Exception, T, bool> _onException = null;
        public SqlTransaction Transaction;
        private bool _needTransaction = false;
        private IsolationLevel _isolationLevel = System.Data.IsolationLevel.ReadCommitted;
        private int? _timeOut = null;

        public SqlConnection Connection;

        protected DbHelper(string text, string connectionString)
        {
            Text = text;
            _connectionString = connectionString;
        }

        protected DbHelper(string text, SqlConnection connection, SqlTransaction transaction)
        {
            Text = text;
            Connection = connection;
            Transaction = transaction;
        }

        protected abstract void BeginExecute(SqlCommand cmd);

        /// <summary>
        /// Add exception handler.
        /// </summary>
        /// <param name="onException">Exception handler, see remarks.</param>
        /// <returns>T, for continuous call.</returns>
        /// <remarks>
        /// The signature of an exception handler is:
        ///     bool ExceptionHandler(Exception ex, T dbHelper);
        /// where
        ///     ex: The exception caught by the Execute method of the dbHelper object.
        ///     dbHelper: The DbHelper object that is executing the command which throws the exception.
        ///     returns: true if you close the transaction and connection yourself and eat the exception,
        ///     otherwise false (you want the db helper do the cleanup, and rethrow the exception).
        /// </remarks>
        public T OnException(Func<Exception, T, bool> onException)
        {
            _onException = onException;
            return (T)this;
        }

        /// <summary>
        /// Execute the command, default as stored procedure
        /// </summary>
        /// <param name="end">finish and clean up. set to false if need to keep the connection and continue with another helper</param>
        /// <returns></returns>
        public virtual T Execute(bool end = true)
        {
            try
            {
                Connect();
                InternalExecute();
                if (end)
                    End();

                return (T)this;
            }
            catch (Exception e)
            {
                var canContinue = false;

                if (_onException != null)
                    canContinue = _onException(e, (T)this);

                if (canContinue)
                    return (T)this;

                // Cannot continue, may be the exception handler is null
                // or the handler returns fasle, clean up and rethrow.
                if (Transaction != null)
                {
                    Transaction.Rollback();
                    Transaction.Dispose();
                }
                if (Connection != null)
                {
                    Connection.Dispose();
                }
                throw;
            }
        }

        /// <summary>
        /// Execute the text as stored procedure
        /// </summary>
        public virtual T ExecuteStoreProcedure(bool end = true)
        {
            
            return this.Execute(true);
        }

        /// <summary>
        /// Execute the text as sql string
        /// </summary>
        public virtual T ExecuteSqlString(bool end = true)
        {
            CommandType = CommandType.Text;
            return this.Execute();
        }

        protected virtual void InternalExecute()
        {
            using (var cmd = Connection.CreateCommand())
            {
                cmd.CommandText = Text;
                cmd.CommandType = CommandType;
                cmd.Parameters.AddRange(Parameters.Values.ToArray());
                if (_timeOut.HasValue)
                {
                    cmd.CommandTimeout = _timeOut.Value;
                }
                BeginExecute(cmd);
            }
        }

        /// <summary>
        /// Open the connection, the connectio will auto open when execute() if not
        /// </summary>
        /// <returns></returns>
        public virtual T Connect()
        {
            if (Connection == null)
            {
                Connection = new SqlConnection(_connectionString);
            }
            if (Connection.State != ConnectionState.Open)
            {
                Connection.Open();
            }
            if (_needTransaction)
            {
                Transaction = Connection.BeginTransaction(_isolationLevel);
            }
            return (T)this;
        }

        /// <summary>
        /// Set the timeout value for the command
        /// </summary>
        /// <param name="timeOut"></param>
        /// <returns></returns>
        public virtual T TimeOut(int timeOut)
        {
            _timeOut = timeOut;
            return (T)this;
        }

        /// <summary>
        /// Set the transaction isolation leve. will be set to transaction on openning, wil be ignored if the transaction is already begun
        /// </summary>
        /// <param name="level"></param>
        /// <returns></returns>
        public virtual T IsolationLevel(IsolationLevel level)
        {
            _isolationLevel = level;
            return (T)this;
        }

        public virtual T BeginTransaction()
        {
            _needTransaction = true;
            if (Connection != null && Transaction == null)
            {
                Transaction = Connection.BeginTransaction(_isolationLevel);
            }
            return (T)this;
        }

        public virtual T CommitTransaction()
        {
            if (Transaction != null)
            {
                Transaction.Commit();
            }
            return (T)this;
        }

        /// <summary>
        /// Finish and clean up
        /// </summary>
        /// <param name="commitTransaction">whether to commit the transaction</param>
        /// <returns></returns>
        public virtual T End(bool commitTransaction = true)
        {
            if (Transaction != null)
            {
                if (commitTransaction)
                {
                    Transaction.Commit();
                }
                Transaction.Dispose();
            }
            if (Connection != null)
            {
                Connection.Dispose();
            }
            return (T)this;
        }

        /// <summary>
        /// Apply an action to the helper itself with out breaking the chain call. just for convinience
        /// </summary>
        /// <param name="action"></param>
        /// <returns></returns>
        public T Do(Action<T> action)
        {
            if (action != null)
            {
                action((T)this);
            }
            return (T)this;
        }

        /// <summary>
        /// Get result from a output parameter and cast to type TV
        /// </summary>
        public T GetParamResult<TV>(string outParamName, out TV value)
        {
            value = GetParamResult<TV>(outParamName);
            return (T)this;
        }

        /// <summary>
        /// Get result from a output parameter and cast to type TV
        /// </summary>
        public TV GetParamResult<TV>(string outParamName)
        {
            if (_outParameters.ContainsKey(outParamName))
            {
                var p = _outParameters[outParamName];
                if (p.Value != DBNull.Value)
                {
                    return (TV)p.Value;
                }
            }
            return default(TV);
        }

        /// <summary>
        /// Get return value and cast to type TV
        /// </summary>
        public T GetReturnValue<TV>(out TV value)
        {
            if (ReturnValue.Value != DBNull.Value)
            {
                value = (TV)ReturnValue.Value;
                return (T)this;
            }
            value = default(TV);
            return (T)this;
        }

        /// <summary>
        /// Get return value and cast to type TV
        /// </summary>
        public TV GetReturnValue<TV>()
        {
            if (ReturnValue.Value != DBNull.Value)
            {
                return (TV)ReturnValue.Value;
            }
            return default(TV);
        }
        
        /// <summary>
        /// Call this when expecting an return value from the stored procedure. and use GetReturnValue() to get value after Execute()
        /// </summary>
        /// <returns></returns>
        public T DefineReturnValue()
        {
            ReturnValue = new SqlParameter() { Direction = ParameterDirection.ReturnValue };
            return AddParam(ReturnValue);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public virtual T RemoveParam(string name)
        {
            if (!name.StartsWith("@"))
            {
                name = "@" + name;
            }
            name = name.ToLower();
            if (Parameters.ContainsKey(name))
            {
                Parameters.Remove(name);
            }
            return (T)this;
        }

        /// <summary>
        /// Add a parameter, the name of the parameter can include or exclude '@'
        /// </summary>
        /// <param name="param"></param>
        /// <returns></returns>
        public virtual T AddParam(SqlParameter param)
        {
            if (param.Direction == ParameterDirection.InputOutput || param.Direction == ParameterDirection.Output)
            {
                _outParameters.Add(param.ParameterName, param);
            }
            if (!param.ParameterName.StartsWith("@"))
            {
                param.ParameterName = "@" + param.ParameterName;
            }
            Parameters[param.ParameterName.ToLower()] = param;
            return (T)this;
        }
    }
}