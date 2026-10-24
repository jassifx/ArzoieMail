// Copyright (c) 2010 Martin Knafve / hMailServer.com.  
// http://www.hmailserver.com

#include "stdafx.h"

#include "MySQLInterface.h"

#include "../Util/FileUtilities.h"

#ifdef _DEBUG
#define DEBUG_NEW new(_NORMAL_BLOCK, __FILE__, __LINE__)
#define new DEBUG_NEW
#endif

namespace HM
{

   MySQLInterface::MySQLInterface() :
      library_instance_(0),
      p_mysql_real_connect(0),
      p_mysql_close(0),
      p_mysql_init(0),
      p_mysql_error(0),
      p_mysql_query(0),
      p_mysql_store_result(0),
      p_mysql_free_result(0),
      p_mysql_insert_id(0),
      p_mysql_errno(0),
      p_mysql_num_rows(0),
      p_mysql_fetch_row(0),
      p_mysql_num_fields(0),
      p_mysql_fetch_field_direct(0),
      p_mysql_get_server_version(0),
      p_mysql_options(0),
      p_mysql_stmt_init(0),
      p_mysql_stmt_prepare(0),
      p_mysql_stmt_bind_param(0),
      p_mysql_stmt_execute(0),
      p_mysql_stmt_result_metadata(0),
      p_mysql_stmt_attr_set(0),
      p_mysql_stmt_store_result(0),
      p_mysql_stmt_bind_result(0),
      p_mysql_stmt_fetch(0),
      p_mysql_stmt_fetch_column(0),
      p_mysql_stmt_close(0),
      p_mysql_stmt_errno(0),
      p_mysql_stmt_error(0),
      p_mysql_stmt_insert_id(0)
   {

   }

   MySQLInterface::~MySQLInterface()
   {
      try
      {
         if (library_instance_)
         {
            FreeLibrary(library_instance_);
         }
      }
      catch (...)
      {

      }
   }

   String
   MySQLInterface::GetLibraryDirectory_()
   {
      LPTSTR szPath = (LPTSTR)alloca( 2048 );
      DWORD  dwPathLength;

      dwPathLength = GetModuleFileName(NULL, szPath, 2048 );
      szPath[ dwPathLength ] = 0; // --- nullterminated.

      String sPath(szPath);

      int iLastSlash = std::max(sPath.ReverseFind(_T("\\")), sPath.ReverseFind(_T("/")));
      return sPath.Mid(0, iLastSlash);
   }

   String
   MySQLInterface::GetLibraryFileName_()
   {
      // MariaDB Connector/C, which the installer puts in the Bin directory with the OpenSSL
      // DLLs it uses for TLS. It is the only MySQL client hMailServer loads: Oracle's
      // libmysql.dll, which an administrator once had to put there, lays out MYSQL_BIND
      // differently from the MariaDB headers MySQLStatement is compiled against.
      return GetLibraryDirectory_() + "\\libmariadb.dll";
   }

   bool
   MySQLInterface::Load(String &sErrorMessage)
   {
      String sLibrary = GetLibraryFileName_();
      library_instance_ = LoadLibrary(sLibrary);

      if (!library_instance_)
      {
         sErrorMessage = Formatter::Format("Error:\r\n"
               "The MySQL client (libmariadb.dll) could not be loaded.\r\n"
               "hMailServer needs MariaDB Connector/C to connect to MySQL or MariaDB.\r\n"
               "This file is installed into the hMailServer Bin directory. If it has been removed, reinstall hMailServer to restore it.\r\n"
               "Path: {0}", sLibrary);

         ErrorManager::Instance()->ReportError(ErrorManager::Critical, 5094, "MySQLInterface::Load", sErrorMessage);

         return false;
      }

      p_mysql_real_connect = (hm_mysql_real_connect*)GetProcAddress( (HMODULE)library_instance_, "mysql_real_connect" );
      p_mysql_close = (hm_mysql_close*) GetProcAddress( (HMODULE)library_instance_, "mysql_close" );
      p_mysql_init = (hm_mysql_init*) GetProcAddress( (HMODULE)library_instance_, "mysql_init" );
      p_mysql_error = (hm_mysql_error*) GetProcAddress( (HMODULE)library_instance_, "mysql_error" );
      p_mysql_query = (hm_mysql_query*) GetProcAddress( (HMODULE)library_instance_, "mysql_query" );
      p_mysql_store_result = (hm_mysql_store_result*) GetProcAddress( (HMODULE)library_instance_, "mysql_store_result" );
      p_mysql_free_result = (hm_mysql_free_result*) GetProcAddress( (HMODULE)library_instance_, "mysql_free_result" );
      p_mysql_insert_id = (hm_mysql_insert_id*) GetProcAddress( (HMODULE)library_instance_, "mysql_insert_id" );
      p_mysql_errno = (hm_mysql_errno*) GetProcAddress( (HMODULE)library_instance_, "mysql_errno" );
      p_mysql_num_rows = (hm_mysql_num_rows*) GetProcAddress( (HMODULE)library_instance_, "mysql_num_rows" );
      p_mysql_fetch_row = (hm_mysql_fetch_row*) GetProcAddress( (HMODULE)library_instance_, "mysql_fetch_row" );
      p_mysql_num_fields = (hm_mysql_num_fields*) GetProcAddress( (HMODULE)library_instance_, "mysql_num_fields" );
      p_mysql_fetch_field_direct = (hm_mysql_fetch_field_direct*) GetProcAddress( (HMODULE)library_instance_, "mysql_fetch_field_direct" );
      p_mysql_get_server_version = (hm_mysql_get_server_version*) GetProcAddress( (HMODULE)library_instance_, "mysql_get_server_version" );
      p_mysql_options = (hm_mysql_options*) GetProcAddress( (HMODULE)library_instance_, "mysql_options" );

      p_mysql_stmt_init = (hm_mysql_stmt_init*) GetProcAddress( (HMODULE)library_instance_, "mysql_stmt_init" );
      p_mysql_stmt_prepare = (hm_mysql_stmt_prepare*) GetProcAddress( (HMODULE)library_instance_, "mysql_stmt_prepare" );
      p_mysql_stmt_bind_param = (hm_mysql_stmt_bind_param*) GetProcAddress( (HMODULE)library_instance_, "mysql_stmt_bind_param" );
      p_mysql_stmt_execute = (hm_mysql_stmt_execute*) GetProcAddress( (HMODULE)library_instance_, "mysql_stmt_execute" );
      p_mysql_stmt_result_metadata = (hm_mysql_stmt_result_metadata*) GetProcAddress( (HMODULE)library_instance_, "mysql_stmt_result_metadata" );
      p_mysql_stmt_attr_set = (hm_mysql_stmt_attr_set*) GetProcAddress( (HMODULE)library_instance_, "mysql_stmt_attr_set" );
      p_mysql_stmt_store_result = (hm_mysql_stmt_store_result*) GetProcAddress( (HMODULE)library_instance_, "mysql_stmt_store_result" );
      p_mysql_stmt_bind_result = (hm_mysql_stmt_bind_result*) GetProcAddress( (HMODULE)library_instance_, "mysql_stmt_bind_result" );
      p_mysql_stmt_fetch = (hm_mysql_stmt_fetch*) GetProcAddress( (HMODULE)library_instance_, "mysql_stmt_fetch" );
      p_mysql_stmt_fetch_column = (hm_mysql_stmt_fetch_column*) GetProcAddress( (HMODULE)library_instance_, "mysql_stmt_fetch_column" );
      p_mysql_stmt_close = (hm_mysql_stmt_close*) GetProcAddress( (HMODULE)library_instance_, "mysql_stmt_close" );
      p_mysql_stmt_errno = (hm_mysql_stmt_errno*) GetProcAddress( (HMODULE)library_instance_, "mysql_stmt_errno" );
      p_mysql_stmt_error = (hm_mysql_stmt_error*) GetProcAddress( (HMODULE)library_instance_, "mysql_stmt_error" );
      p_mysql_stmt_insert_id = (hm_mysql_stmt_insert_id*) GetProcAddress( (HMODULE)library_instance_, "mysql_stmt_insert_id" );

      // Every function is required, so a client that lacks one - not MariaDB Connector/C, or a
      // version too old - fails here, naming what it lacks, rather than when it is first used.
      const std::pair<const void *, const char *> exports[] =
      {
         { p_mysql_real_connect, "mysql_real_connect" }, { p_mysql_close, "mysql_close" },
         { p_mysql_init, "mysql_init" }, { p_mysql_error, "mysql_error" }, { p_mysql_query, "mysql_query" },
         { p_mysql_store_result, "mysql_store_result" }, { p_mysql_free_result, "mysql_free_result" },
         { p_mysql_insert_id, "mysql_insert_id" }, { p_mysql_errno, "mysql_errno" },
         { p_mysql_num_rows, "mysql_num_rows" }, { p_mysql_fetch_row, "mysql_fetch_row" },
         { p_mysql_num_fields, "mysql_num_fields" }, { p_mysql_fetch_field_direct, "mysql_fetch_field_direct" },
         { p_mysql_get_server_version, "mysql_get_server_version" }, { p_mysql_options, "mysql_options" },
         { p_mysql_stmt_init, "mysql_stmt_init" }, { p_mysql_stmt_prepare, "mysql_stmt_prepare" },
         { p_mysql_stmt_bind_param, "mysql_stmt_bind_param" }, { p_mysql_stmt_execute, "mysql_stmt_execute" },
         { p_mysql_stmt_result_metadata, "mysql_stmt_result_metadata" }, { p_mysql_stmt_attr_set, "mysql_stmt_attr_set" },
         { p_mysql_stmt_store_result, "mysql_stmt_store_result" }, { p_mysql_stmt_bind_result, "mysql_stmt_bind_result" },
         { p_mysql_stmt_fetch, "mysql_stmt_fetch" }, { p_mysql_stmt_fetch_column, "mysql_stmt_fetch_column" },
         { p_mysql_stmt_close, "mysql_stmt_close" }, { p_mysql_stmt_errno, "mysql_stmt_errno" },
         { p_mysql_stmt_error, "mysql_stmt_error" }, { p_mysql_stmt_insert_id, "mysql_stmt_insert_id" },
      };

      for (const auto &required : exports)
      {
         if (required.first)
            continue;

         sErrorMessage = Formatter::Format("Error:\r\n"
               "The MySQL client ({0}) does not export {1}, which hMailServer needs.\r\n"
               "The file must be the MariaDB Connector/C that the hMailServer installer puts in the Bin directory. Reinstall hMailServer to restore it.",
               sLibrary, String(required.second));

         ErrorManager::Instance()->ReportError(ErrorManager::Critical, 5094, "MySQLInterface::Load", sErrorMessage);

         FreeLibrary(library_instance_);
         library_instance_ = 0;
         return false;
      }

      return true;
   }

   bool
   MySQLInterface::IsLoaded()
   {
      if (library_instance_ != nullptr)
         return true;
      else
         return false;

   }
}
