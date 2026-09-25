package com.joyliveex.vjc.daemon;

/**
 * Wire constants — MUST mirror desktop/src/VirtualJoyCon.Network/Protocol/Wire.cs
 * and android app WireConsts.kt exactly.
 */
public final class Constants {
    private Constants() {}

    public static final byte VERSION = 1;
    public static final int TAG = 16;
    public static final int OVERHEAD = 12;              // ver1 type1 sess4 seq4 encLen2
    public static final int PBKDF2_ITERATIONS = 60_000;
    public static final int STATE_SIZE = 20;            // 4+2+2+2+2+1+1+1+1+4

    public static final byte TYPE_SALT_C = 0x01;
    public static final byte TYPE_SALT_S = 0x02;
    public static final byte TYPE_ERROR = 0x03;
    public static final byte TYPE_HELLO = 0x04;
    public static final byte TYPE_WELCOME = 0x05;
    public static final byte TYPE_STATE_D = 0x10;
    public static final byte TYPE_STATE_S = 0x11;
    public static final byte TYPE_PING = 0x12;
    public static final byte TYPE_PONG = 0x13;
    public static final byte TYPE_HEARTBEAT = 0x14;
    public static final byte TYPE_CMD = 0x20;
    public static final byte TYPE_STATUS = 0x21;
    public static final byte TYPE_LOG = 0x22;

    public static final byte ROLE_DAEMON = 1;

    public static final byte CMD_VIBRATE = 1;
    public static final byte CMD_STOP_DAEMON = 3;
    public static final byte CMD_RELEASE_ALL = 7;

    // Virtual Joy-Con button bits (mirror of ButtonFlags)
    public static final int B_A = 1;
    public static final int B_B = 1 << 1;
    public static final int B_X = 1 << 2;
    public static final int B_Y = 1 << 3;
    public static final int B_L = 1 << 4;
    public static final int B_R = 1 << 5;
    public static final int B_ZL = 1 << 6;
    public static final int B_ZR = 1 << 7;
    public static final int B_MINUS = 1 << 8;
    public static final int B_PLUS = 1 << 9;
    public static final int B_HOME = 1 << 10;
    public static final int B_CAPTURE = 1 << 11;
    public static final int B_L3 = 1 << 12;
    public static final int B_R3 = 1 << 13;
    public static final int B_DUP = 1 << 14;
    public static final int B_DDOWN = 1 << 15;
    public static final int B_DLEFT = 1 << 16;
    public static final int B_DRIGHT = 1 << 17;
    public static final int B_SL_L = 1 << 18;
    public static final int B_SR_L = 1 << 19;
    public static final int B_SL_R = 1 << 20;
    public static final int B_SR_R = 1 << 21;

    // Android keycode names resolved by reflection (never hardcode numbers!)
    public static final String KEY_A = "KEYCODE_BUTTON_A";
    public static final String KEY_B = "KEYCODE_BUTTON_B";
    public static final String KEY_X = "KEYCODE_BUTTON_X";
    public static final String KEY_Y = "KEYCODE_BUTTON_Y";
    public static final String KEY_L1 = "KEYCODE_BUTTON_L1";
    public static final String KEY_R1 = "KEYCODE_BUTTON_R1";
    public static final String KEY_L2 = "KEYCODE_BUTTON_L2";
    public static final String KEY_R2 = "KEYCODE_BUTTON_R2";
    public static final String KEY_THUMBL = "KEYCODE_BUTTON_THUMBL";
    public static final String KEY_THUMBR = "KEYCODE_BUTTON_THUMBR";
    public static final String KEY_START = "KEYCODE_BUTTON_START";
    public static final String KEY_SELECT = "KEYCODE_BUTTON_SELECT";
    public static final String KEY_HOME = "KEYCODE_HOME";
    public static final String KEY_DUP = "KEYCODE_DPAD_UP";
    public static final String KEY_DDOWN = "KEYCODE_DPAD_DOWN";
    public static final String KEY_DLEFT = "KEYCODE_DPAD_LEFT";
    public static final String KEY_DRIGHT = "KEYCODE_DPAD_RIGHT";
}
