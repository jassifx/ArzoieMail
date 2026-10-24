// Copyright (c) 2005 Martin Knafve / hMailServer.com.  
// http://www.hmailserver.com

#pragma once

// No DEBUG_NEW here. The .cpp files define it after their includes; defined in a header,
// it would also rewrite the placement new of every header included after this one, such
// as Boost's.

namespace HM   
{
   enum PersistenceMode
   {
      PersistenceModeNormal = 0,
      PersistenceModeRestore = 1,
      PersistenceModeRename = 2,
   };
   
}