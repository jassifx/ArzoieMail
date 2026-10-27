---
title: "COM API"
slug: com_objects
parent: book_other
index: 0
is_book: false
---

## Legacy management compatibility API

ArzoieMail retains the `hMailServer.Application` COM API, type library and related identifiers
for existing desktop tools, database setup and scripts. The inherited PHP panel also uses this
compatibility surface during migration. These reference pages describe COM objects, not the
replacement web panel's REST contract.

The modern ASP.NET Core panel reaches the business layer through versioned REST and a restricted
native channel, without COM or direct database access. See the current management architecture
and API README for that boundary. PowerShell event-host replacement is a later phase; existing
VBScript/JScript examples here are compatibility examples, not the future scripting design.

## Existing client sequence

1. Create the compatibility application object. A VBScript client uses
   `CreateObject("hMailServer.Application")`; that ProgID is unchanged.
2. Call `Application.Authenticate` with valid credentials before accessing server settings.
3. Use the authorized objects/properties for the caller's server/domain/account permissions.

Existing .NET clients retain the `hMailServer` namespace and `Interop.hMailServer.dll` identity.
Do not rename these identifiers when updating human-facing product names. COM activation rights
remain an independent configuration concern; the replacement API hosting identity needs no DCOM grant.
