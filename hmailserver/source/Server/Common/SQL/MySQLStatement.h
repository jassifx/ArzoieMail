// Copyright (c) 2010 Martin Knafve / hMailServer.com.
// http://www.hmailserver.com

#pragma once

#include <optional>

#include "MySQLInterface.h"

namespace HM
{
   class SQLCommand;

   // Runs a command with parameters as a prepared statement of MariaDB Connector/C, so that
   // its values are bound rather than written into the SQL. The only file that includes the
   // MariaDB headers: everywhere else the client is reached through MySQLInterface.
   class MySQLStatement
   {
   public:

      // A result set, read in full. A column value is empty when it is NULL.
      struct Rows
      {
         std::vector<AnsiString> columns;
         std::vector<std::vector<std::optional<std::string>>> values;
      };

      explicit MySQLStatement(hm_MYSQL *connection);
      ~MySQLStatement();

      MySQLStatement(const MySQLStatement &) = delete;
      MySQLStatement &operator=(const MySQLStatement &) = delete;

      // Prepares, binds and executes the command. rows, when given, receives the result set,
      // and insertId the id of a row it inserted. On failure, errorMessage and
      // GetErrorNumber() say why.
      bool Execute(const SQLCommand &command, Rows *rows, __int64 *insertId, String &errorMessage);

      // The MySQL error number of the failure, as mysql_errno() would give it.
      unsigned int GetErrorNumber() const { return error_number_; }

   private:

      bool Fail_(const char *step, String &errorMessage);
      bool ReadRows_(Rows &rows, String &errorMessage);

      hm_MYSQL *connection_;
      hm_MYSQL_STMT *statement_;
      unsigned int error_number_;
   };
}
