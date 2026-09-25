package com.joyliveex.vjc.daemon;

import java.lang.reflect.Constructor;
import java.lang.reflect.Field;
import java.lang.reflect.Method;

/**
 * System-wide gamepad injection, run as the shell uid.
 *
 * Every Android API used here is resolved by reflection:
 *  - InputManager.injectInputEvent(InputEvent, mode) — granted to shell
 *    (exactly what `adb shell input` uses), invisible to normal apps.
 *  - KeyEvent public 10-arg constructor (downTime,eventTime,action,code,repeat,
 *    metaState,deviceId,scanCode,flags,SOURCE_GAMEPAD) — marks keys as gamepad.
 *  - MotionEvent.Builder with setSource(SOURCE_GAMEPAD|SOURCE_JOYSTICK) and a
 *    PointerCoords carrying AXIS_X/Y (left stick), AXIS_Z/RZ (right stick),
 *    AXIS_LTRIGGER/RTRIGGER (ZL/ZR analog), AXIS_HAT_X/Y (d-pad).
 * Keycodes are read from android.view.KeyEvent fields, never hardcoded.
 */
public final class Injector {

    private static final int DEVICE_ID_VIRTUAL = -1;

    private Object inputManager;              // IInputManager binder proxy
    private Method injectMethod;
    private Method uptimeMethod;              // SystemClock.uptimeMillis
    private Class<?> motionEventCls;
    private Class<?> builderCls;
    private Class<?> pointerCoordsCls;
    private Method builderAddPointer;
    private Method setAxisMethod;

    private int ACTION_MOVE, SOURCE_GAMEPAD, SOURCE_JOYSTICK;
    private int AXIS_X, AXIS_Y, AXIS_Z, AXIS_RZ, AXIS_LT, AXIS_RT, AXIS_HX, AXIS_HY;
    private int KEY_A, KEY_B, KEY_X, KEY_Y, KEY_L1, KEY_R1, KEY_L2, KEY_R2;
    private int KEY_THUMBL, KEY_THUMBR, KEY_START, KEY_SELECT, KEY_HOME;
    private int KEY_DUP, KEY_DDOWN, KEY_DLEFT, KEY_DRIGHT;
    private Object context;                  // system context (best effort)
    private Method vibratorVibrate;
    private Object vibrator;

    private int heldButtons;                  // currently-held virtual button state
    private int lastHat;
    private boolean dPadDownA, dPadDownB, dPadDownC, dPadDownD; // dpad key ids held
    private boolean ready;
    private String error;

    public boolean isReady() { return ready; }
    public String getError() { return error; }

    public boolean init() {
        try {
            Class<?> sm = Class.forName("android.os.ServiceManager");
            Object binder = sm.getMethod("getService", String.class).invoke(null, "input");
            Class<?> stub = Class.forName("android.hardware.input.IInputManager$Stub");
            inputManager = stub.getMethod("asInterface", Class.forName("android.os.IBinder")).invoke(null, binder);
            Class<?> inputEventCls = Class.forName("android.view.InputEvent");
            injectMethod = inputManager.getClass().getMethod("injectInputEvent", inputEventCls, int.class);

            uptimeMethod = Class.forName("android.os.SystemClock").getMethod("uptimeMillis");

            motionEventCls = Class.forName("android.view.MotionEvent");
            builderCls = Class.forName("android.view.MotionEvent$Builder");
            pointerCoordsCls = Class.forName("android.view.MotionEvent$PointerCoords");
            setAxisMethod = pointerCoordsCls.getMethod("setAxisValue", int.class, float.class);
            builderAddPointer = builderCls.getMethod("addPointer", int.class, pointerCoordsCls, float.class, float.class);

            ACTION_MOVE = fieldInt(motionEventCls, "ACTION_MOVE");
            AXIS_X = fieldInt(motionEventCls, "AXIS_X");
            AXIS_Y = fieldInt(motionEventCls, "AXIS_Y");
            AXIS_Z = fieldInt(motionEventCls, "AXIS_Z");
            AXIS_RZ = fieldInt(motionEventCls, "AXIS_RZ");
            AXIS_LT = fieldInt(motionEventCls, "AXIS_LTRIGGER");
            AXIS_RT = fieldInt(motionEventCls, "AXIS_RTRIGGER");
            AXIS_HX = fieldInt(motionEventCls, "AXIS_HAT_X");
            AXIS_HY = fieldInt(motionEventCls, "AXIS_HAT_Y");

            Class<?> dev = Class.forName("android.view.InputDevice");
            SOURCE_GAMEPAD = fieldInt(dev, "SOURCE_GAMEPAD");
            SOURCE_JOYSTICK = fieldInt(dev, "SOURCE_JOYSTICK");

            Class<?> keyEvt = Class.forName("android.view.KeyEvent");
            KEY_A = fieldInt(keyEvt, Constants.KEY_A);
            KEY_B = fieldInt(keyEvt, Constants.KEY_B);
            KEY_X = fieldInt(keyEvt, Constants.KEY_X);
            KEY_Y = fieldInt(keyEvt, Constants.KEY_Y);
            KEY_L1 = fieldInt(keyEvt, Constants.KEY_L1);
            KEY_R1 = fieldInt(keyEvt, Constants.KEY_R1);
            KEY_L2 = fieldInt(keyEvt, Constants.KEY_L2);
            KEY_R2 = fieldInt(keyEvt, Constants.KEY_R2);
            KEY_THUMBL = fieldInt(keyEvt, Constants.KEY_THUMBL);
            KEY_THUMBR = fieldInt(keyEvt, Constants.KEY_THUMBR);
            KEY_START = fieldInt(keyEvt, Constants.KEY_START);
            KEY_SELECT = fieldInt(keyEvt, Constants.KEY_SELECT);
            KEY_HOME = fieldInt(keyEvt, Constants.KEY_HOME);
            KEY_DUP = fieldInt(keyEvt, Constants.KEY_DUP);
            KEY_DDOWN = fieldInt(keyEvt, Constants.KEY_DDOWN);
            KEY_DLEFT = fieldInt(keyEvt, Constants.KEY_DLEFT);
            KEY_DRIGHT = fieldInt(keyEvt, Constants.KEY_DRIGHT);

            tryInitContext();
            ready = true;
            return true;
        } catch (Throwable t) {
            error = t.toString();
            ready = false;
            return false;
        }
    }

    private static int fieldInt(Class<?> c, String name) throws Exception {
        Field f = c.getField(name);
        return f.getInt(null);
    }

    private void tryInitContext() {
        try {
            Class<?> at = Class.forName("android.app.ActivityThread");
            Object thread = at.getMethod("systemMain").invoke(null);
            context = at.getMethod("getSystemContext").invoke(thread);
            Object vmgr = context.getClass().getMethod("getSystemService", String.class)
                    .invoke(context, ContextNames.VIBRATOR_SERVICE);
            if (vmgr != null) {
                try {
                    vibrator = vmgr;
                    vibratorVibrate = vmgr.getClass().getMethod("vibrate", long.class);
                } catch (NoSuchMethodException e) {
                    // newer API: vibrate(VibrationEffect, VibDuration) — try simple fallback
                    try {
                        Class<?> effect = Class.forName("android.os.VibrationEffect");
                        Method createOne = effect.getMethod("createOneShot", long.class, int.class);
                        Method vib = vmgr.getClass().getMethod("vibrate", effect,
                                Class.forName("android.media.AudioAttributes"));
                        vibrator = vmgr;
                        vibratorVibrate = null;
                        vibratorEffectMethod = vib;
                        vibratorEffectFactory = createOne;
                        vibrator = vmgr;
                    } catch (Throwable ignored) { }
                }
            }
        } catch (Throwable t) {
            context = null;
        }
    }

    private Method vibratorEffectMethod;
    private Method vibratorEffectFactory;

    private static final class ContextNames {
        static final String VIBRATOR_SERVICE = "vibrator";
    }

    // ------------------------------------------------------------------
    // state application

    public void apply(State s) {
        if (!ready) return;
        int nb = s.buttons;
        int diff = nb ^ heldButtons;

        for (int bit = 0; bit < 32; bit++) {
            int mask = 1 << bit;
            if ((diff & mask) == 0) continue;
            int key = keyFor(mask);
            boolean down = (nb & mask) != 0;
            if (key == 0) {
                // rail buttons SL/SR & capture: no global keycode — emit only in motion state
                continue;
            }
            injectKey(key, down);
        }
        heldButtons = nb;

        // d-pad: also drive DPAD keycodes (some games read keys, some read hat axes)
        injectDpad(s);
        // analog axes: always one MOVE event mirroring current state
        injectAxes(s);
    }

    private int keyFor(int mask) {
        if (mask == Constants.B_A) return KEY_A;
        if (mask == Constants.B_B) return KEY_B;
        if (mask == Constants.B_X) return KEY_X;
        if (mask == Constants.B_Y) return KEY_Y;
        if (mask == Constants.B_L) return KEY_L1;
        if (mask == Constants.B_R) return KEY_R1;
        if (mask == Constants.B_ZL) return KEY_L2;
        if (mask == Constants.B_ZR) return KEY_R2;
        if (mask == Constants.B_L3) return KEY_THUMBL;
        if (mask == Constants.B_R3) return KEY_THUMBR;
        if (mask == Constants.B_PLUS) return KEY_START;
        if (mask == Constants.B_MINUS) return KEY_SELECT;
        if (mask == Constants.B_HOME) return KEY_HOME;
        return 0;
    }

    private void injectDpad(State s) {
        boolean up = (s.buttons & Constants.B_DUP) != 0;
        boolean down = (s.buttons & Constants.B_DDOWN) != 0;
        boolean left = (s.buttons & Constants.B_DLEFT) != 0;
        boolean right = (s.buttons & Constants.B_DRIGHT) != 0;
        if (up != dPadDownA) { injectKey(KEY_DUP, up); dPadDownA = up; }
        if (down != dPadDownB) { injectKey(KEY_DDOWN, down); dPadDownB = down; }
        if (left != dPadDownC) { injectKey(KEY_DLEFT, left); dPadDownC = left; }
        if (right != dPadDownD) { injectKey(KEY_DRIGHT, right); dPadDownD = right; }
    }

    private void injectKey(int code, boolean down) {
        try {
            long t = (long) uptimeMethod.invoke(null);
            Constructor<?> ctor = Class.forName("android.view.KeyEvent").getConstructor(
                    long.class, long.class, int.class, int.class, int.class,
                    int.class, int.class, int.class, int.class, int.class);
            int action = down ? 0 : 1; // KEY_ACTION_DOWN / UP are 0/1 (kept numeric to avoid another lookup)
            Object ev = ctor.newInstance(t, t, action, code, 0, 0, DEVICE_ID_VIRTUAL, 0, 0,
                    SOURCE_GAMEPAD | SOURCE_JOYSTICK);
            injectMethod.invoke(inputManager, ev, 0); // ASYNC mode: lowest latency
        } catch (Throwable ignored) { }
    }

    private void injectAxes(State s) {
        try {
            long t = (long) uptimeMethod.invoke(null);
            BuilderSpec b = new BuilderSpec(t);
            Object coords = pointerCoordsCls.getDeclaredConstructor().newInstance();
            setAxisMethod.invoke(coords, AXIS_X, s.lx);
            setAxisMethod.invoke(coords, AXIS_Y, s.ly);
            setAxisMethod.invoke(coords, AXIS_Z, s.rx);
            setAxisMethod.invoke(coords, AXIS_RZ, s.ry);
            setAxisMethod.invoke(coords, AXIS_LT, s.lt);
            setAxisMethod.invoke(coords, AXIS_RT, s.rt);
            float hx = (s.buttons & Constants.B_DRIGHT) != 0 ? 1f : ((s.buttons & Constants.B_DLEFT) != 0 ? -1f : 0f);
            float hy = (s.buttons & Constants.B_DDOWN) != 0 ? 1f : ((s.buttons & Constants.B_DUP) != 0 ? -1f : 0f);
            setAxisMethod.invoke(coords, AXIS_HX, hx);
            setAxisMethod.invoke(coords, AXIS_HY, hy);

            builderAddPointer.invoke(b.builder, 0, coords, 1f, 1f);
            Object ev = b.build();
            injectMethod.invoke(inputManager, ev, 0);
        } catch (Throwable ignored) { }
    }

    private final class BuilderSpec {
        final Object builder;
        BuilderSpec(long t) throws Exception {
            builder = builderCls.getDeclaredConstructor().newInstance();
            builderCls.getMethod("setDownTime", long.class).invoke(builder, t);
            builderCls.getMethod("setEventTime", long.class).invoke(builder, t);
            builderCls.getMethod("setAction", int.class).invoke(builder, ACTION_MOVE);
            builderCls.getMethod("setSource", int.class).invoke(builder, SOURCE_GAMEPAD | SOURCE_JOYSTICK);
            tryCall("setDeviceId", int.class, DEVICE_ID_VIRTUAL);
            tryCall("setButtonState", int.class, androidButtonState(heldButtons));
            tryCall("setDisplayId", int.class, 0);
            tryCall("setEdgeFlags", int.class, 0);
        }

        private void tryCall(String name, Class<?> p, Object v) {
            try {
                builderCls.getMethod(name, p).invoke(builder, v);
            } catch (Throwable ignored) {
                // method absent on this API level — not fatal
            }
        }
        Object build() throws Exception {
            return builderCls.getMethod("build").invoke(builder);
        }
    }

    private int androidButtonState(int buttons) {
        int bs = 0;
        try {
            if ((buttons & Constants.B_A) != 0) bs |= fieldInt(motionEventCls, "BUTTON_A");
            if ((buttons & Constants.B_B) != 0) bs |= fieldInt(motionEventCls, "BUTTON_B");
            if ((buttons & Constants.B_X) != 0) bs |= fieldInt(motionEventCls, "BUTTON_X");
            if ((buttons & Constants.B_Y) != 0) bs |= fieldInt(motionEventCls, "BUTTON_Y");
            if ((buttons & Constants.B_L) != 0) bs |= fieldInt(motionEventCls, "BUTTON_L1");
            if ((buttons & Constants.B_R) != 0) bs |= fieldInt(motionEventCls, "BUTTON_R1");
        } catch (Throwable ignored) { }
        return bs;
    }

    /** Release everything (called on link loss — never leave a virtual button held). */
    public void releaseAll(State zero) {
        heldButtons = 0;
        lastHat = 0;
        if (dPadDownA) { injectKey(KEY_DUP, false); dPadDownA = false; }
        if (dPadDownB) { injectKey(KEY_DDOWN, false); dPadDownB = false; }
        if (dPadDownC) { injectKey(KEY_DLEFT, false); dPadDownC = false; }
        if (dPadDownD) { injectKey(KEY_DRIGHT, false); dPadDownD = false; }
        injectAxes(zero);
    }

    public void vibrate(int ms) {
        try {
            if (vibratorVibrate != null && vibrator != null) {
                vibratorVibrate.invoke(vibrator, (long) ms);
            } else if (vibratorEffectMethod != null && vibrator != null && context != null) {
                Object effect = vibratorEffectFactory.invoke(null, (long) ms, 255 /*amplitude default*/);
                vibratorEffectMethod.invoke(vibrator, effect, null);
            }
        } catch (Throwable ignored) { }
    }

    public static final class State {
        public int buttons;
        public float lx, ly, rx, ry, lt, rt;
    }
}
