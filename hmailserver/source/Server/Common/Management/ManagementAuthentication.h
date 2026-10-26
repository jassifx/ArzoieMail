#pragma once

namespace HM
{
   class Account;

   // Business-layer authentication shared by the native API and the compatibility adapter.
   // Only the compatibility adapter may permit an unset installation administrator password.
   class ManagementAuthentication
   {
   public:
      static std::shared_ptr<const Account> Authenticate(const String &username, const String &password,
                                                        bool allowUnsetAdministrator = false);
   };
}
