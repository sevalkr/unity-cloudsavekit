// CloudSaveKit - iCloud key-value store bridge.
//
// Wraps NSUbiquitousKeyValueStore for Unity. Chosen over GameKit's GKSavedGame on
// purpose: KVS works for every user with an iCloud account, without any Game Center
// sign-in prompt. The trade-off is the size limit (1 MB total across all keys),
// which the C# side enforces with a clear error before data ever reaches here.
//
// Memory contract: buffers returned by _CloudSaveKit_Load / _CloudSaveKit_ListKeys
// are malloc'd here and MUST be released by the caller via _CloudSaveKit_Free.

#import <Foundation/Foundation.h>

typedef void (*CloudSaveKitRemoteChangeCallback)(const char* key, int changeReason);

static CloudSaveKitRemoteChangeCallback g_remoteChangeCallback = NULL;
static id g_observerToken = nil;

static char* CloudSaveKit_CopyCString(NSString* string)
{
    const char* utf8 = [string UTF8String];
    if (utf8 == NULL)
    {
        return NULL;
    }
    size_t length = strlen(utf8) + 1;
    char* copy = (char*)malloc(length);
    if (copy != NULL)
    {
        memcpy(copy, utf8, length);
    }
    return copy;
}

bool _CloudSaveKit_IsAvailable(void)
{
    // Non-nil ubiquity identity token == an iCloud account is signed in on this device.
    return [[NSFileManager defaultManager] ubiquityIdentityToken] != nil;
}

bool _CloudSaveKit_Save(const char* key, const unsigned char* bytes, int length)
{
    if (key == NULL || bytes == NULL || length < 0)
    {
        return false;
    }
    NSString* nsKey = [NSString stringWithUTF8String:key];
    NSData* data = [NSData dataWithBytes:bytes length:(NSUInteger)length];
    NSUbiquitousKeyValueStore* store = [NSUbiquitousKeyValueStore defaultStore];
    [store setData:data forKey:nsKey];
    // synchronize() persists to the local on-disk cache immediately and schedules the
    // upload; it does NOT block on the network. Returning false signals a local problem
    // (e.g. quota), which the C# side surfaces as a failed push.
    return [store synchronize];
}

unsigned char* _CloudSaveKit_Load(const char* key, int* outLength)
{
    if (outLength != NULL)
    {
        *outLength = 0;
    }
    if (key == NULL || outLength == NULL)
    {
        return NULL;
    }
    NSString* nsKey = [NSString stringWithUTF8String:key];
    NSData* data = [[NSUbiquitousKeyValueStore defaultStore] dataForKey:nsKey];
    if (data == nil || data.length == 0)
    {
        return NULL;
    }
    unsigned char* buffer = (unsigned char*)malloc(data.length);
    if (buffer == NULL)
    {
        return NULL;
    }
    memcpy(buffer, data.bytes, data.length);
    *outLength = (int)data.length;
    return buffer;
}

bool _CloudSaveKit_Delete(const char* key)
{
    if (key == NULL)
    {
        return false;
    }
    NSString* nsKey = [NSString stringWithUTF8String:key];
    NSUbiquitousKeyValueStore* store = [NSUbiquitousKeyValueStore defaultStore];
    bool existed = [store dataForKey:nsKey] != nil;
    if (existed)
    {
        [store removeObjectForKey:nsKey];
        [store synchronize];
    }
    return existed;
}

// Returns all keys with the given prefix, '\n'-separated, or NULL when none exist.
char* _CloudSaveKit_ListKeys(const char* prefix)
{
    NSString* nsPrefix = prefix != NULL ? [NSString stringWithUTF8String:prefix] : @"";
    NSDictionary* all = [[NSUbiquitousKeyValueStore defaultStore] dictionaryRepresentation];
    NSMutableArray<NSString*>* matches = [NSMutableArray array];
    for (NSString* key in all.allKeys)
    {
        if (nsPrefix.length == 0 || [key hasPrefix:nsPrefix])
        {
            [matches addObject:key];
        }
    }
    if (matches.count == 0)
    {
        return NULL;
    }
    return CloudSaveKit_CopyCString([matches componentsJoinedByString:@"\n"]);
}

void _CloudSaveKit_Free(void* pointer)
{
    if (pointer != NULL)
    {
        free(pointer);
    }
}

// Registers the remote-change observer and kicks an initial sync so that data written
// by other devices while the app was closed becomes visible as soon as possible.
// The callback may be invoked on a non-main thread; the C# side documents this.
void _CloudSaveKit_SetRemoteChangeCallback(CloudSaveKitRemoteChangeCallback callback)
{
    g_remoteChangeCallback = callback;

    if (g_observerToken != nil)
    {
        return; // Observer already registered; only the callback pointer was updated.
    }

    NSUbiquitousKeyValueStore* store = [NSUbiquitousKeyValueStore defaultStore];
    g_observerToken = [[NSNotificationCenter defaultCenter]
        addObserverForName:NSUbiquitousKeyValueStoreDidChangeExternallyNotification
                    object:store
                     queue:nil
                usingBlock:^(NSNotification* notification) {
            if (g_remoteChangeCallback == NULL)
            {
                return;
            }
            NSNumber* reason = notification.userInfo[NSUbiquitousKeyValueStoreChangeReasonKey];
            NSArray<NSString*>* changedKeys = notification.userInfo[NSUbiquitousKeyValueStoreChangedKeysKey];
            int reasonValue = reason != nil ? reason.intValue : -1;
            for (NSString* key in changedKeys)
            {
                const char* keyUtf8 = [key UTF8String];
                if (keyUtf8 != NULL)
                {
                    g_remoteChangeCallback(keyUtf8, reasonValue);
                }
            }
        }];

    [store synchronize];
}
