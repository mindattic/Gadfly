var __defProp = Object.defineProperty;
var __getOwnPropNames = Object.getOwnPropertyNames;
var __esm = (fn2, res, err) => function __init() {
  if (err) throw err[0];
  try {
    return fn2 && (res = (0, fn2[__getOwnPropNames(fn2)[0]])(fn2 = 0)), res;
  } catch (e2) {
    throw err = [e2], e2;
  }
};
var __export = (target, all) => {
  for (var name in all)
    __defProp(target, name, { get: all[name], enumerable: true });
};

// node_modules/@mediapipe/tasks-vision/vision_bundle.mjs
var vision_bundle_exports = {};
__export(vision_bundle_exports, {
  DrawingUtils: () => Mu,
  FaceDetector: () => rc,
  FaceLandmarker: () => pc,
  FilesetResolver: () => ih,
  GestureRecognizer: () => yc,
  HandLandmarker: () => Ac,
  HolisticLandmarker: () => Sc,
  ImageClassifier: () => Lc,
  ImageEmbedder: () => xc,
  ImageSegmenter: () => Nc,
  ImageSegmenterResult: () => Rc,
  InteractiveSegmenter: () => Dc,
  InteractiveSegmenterLegacy: () => $c,
  InteractiveSegmenterLegacyResult: () => Pc,
  MPImage: () => Hu,
  MPMask: () => bu,
  ObjectDetector: () => Uc,
  PoseLandmarker: () => Vc,
  TaskRunner: () => ru,
  VisionTaskRunner: () => ec
});
function e(e2, r2) {
  e2 = e2.split(".");
  for (var n2, i2 = t; e2.length && (n2 = e2.shift()); ) e2.length || void 0 === r2 ? i2 = i2[n2] && i2[n2] !== Object.prototype[n2] ? i2[n2] : i2[n2] = {} : i2[n2] = r2;
}
function r() {
  throw Error("Invalid UTF8");
}
function n(t2, e2) {
  return e2 = String.fromCharCode.apply(null, e2), null == t2 ? e2 : t2 + e2;
}
function l(t2) {
  if (c) t2 = (s ||= new TextEncoder()).encode(t2);
  else {
    let r2 = 0, n2 = new Uint8Array(3 * t2.length);
    for (let i2 = 0; i2 < t2.length; i2++) {
      var e2 = t2.charCodeAt(i2);
      if (e2 < 128) n2[r2++] = e2;
      else {
        if (e2 < 2048) n2[r2++] = e2 >> 6 | 192;
        else {
          if (e2 >= 55296 && e2 <= 57343) {
            if (e2 <= 56319 && i2 < t2.length) {
              let s2 = t2.charCodeAt(++i2);
              if (s2 >= 56320 && s2 <= 57343) {
                e2 = 1024 * (e2 - 55296) + s2 - 56320 + 65536, n2[r2++] = e2 >> 18 | 240, n2[r2++] = e2 >> 12 & 63 | 128, n2[r2++] = e2 >> 6 & 63 | 128, n2[r2++] = 63 & e2 | 128;
                continue;
              }
              i2--;
            }
            e2 = 65533;
          }
          n2[r2++] = e2 >> 12 | 224, n2[r2++] = e2 >> 6 & 63 | 128;
        }
        n2[r2++] = 63 & e2 | 128;
      }
    }
    t2 = r2 === n2.length ? n2 : n2.subarray(0, r2);
  }
  return t2;
}
function g() {
  var e2 = t.navigator;
  return e2 && (e2 = e2.userAgent) ? e2 : "";
}
function v(t2) {
  return v[" "](t2), t2;
}
function E(t2) {
  var e2 = t2.length, r2 = 3 * e2 / 4;
  r2 % 3 ? r2 = Math.floor(r2) : -1 != "=.".indexOf(t2[e2 - 1]) && (r2 = -1 != "=.".indexOf(t2[e2 - 2]) ? r2 - 2 : r2 - 1);
  var n2 = new Uint8Array(r2), i2 = 0;
  return (function(t3, e3) {
    function r3(e4) {
      for (; n3 < t3.length; ) {
        let e5 = t3.charAt(n3++), r4 = y[e5];
        if (null != r4) return r4;
        if (!/^[\s\xa0]*$/.test(e5)) throw Error("Unknown base64 encoding at char: " + e5);
      }
      return e4;
    }
    A();
    for (var n3 = 0; ; ) {
      let t4 = r3(-1), n4 = r3(0), i3 = r3(64), s2 = r3(64);
      if (64 === s2 && -1 === t4) break;
      e3(t4 << 2 | n4 >> 4), 64 != i3 && (e3(n4 << 4 & 240 | i3 >> 2), 64 != s2 && e3(i3 << 6 & 192 | s2));
    }
  })(t2, function(t3) {
    n2[i2++] = t3;
  }), i2 !== r2 ? n2.subarray(0, i2) : n2;
}
function A() {
  if (!y) {
    y = {};
    var t2 = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789".split(""), e2 = ["+/=", "+/", "-_=", "-_.", "-_"];
    for (let r2 = 0; r2 < 5; r2++) {
      let n2 = t2.concat(e2[r2].split(""));
      _[r2] = n2;
      for (let t3 = 0; t3 < n2.length; t3++) {
        let e3 = n2[t3];
        void 0 === y[e3] && (y[e3] = t3);
      }
    }
  }
}
function S(t2) {
  return k[t2] || "";
}
function L(t2) {
  if (!T) return E(t2);
  t2 = b.test(t2) ? t2.replace(b, S) : t2, t2 = atob(t2);
  var e2 = new Uint8Array(t2.length);
  for (let r2 = 0; r2 < t2.length; r2++) e2[r2] = t2.charCodeAt(r2);
  return e2;
}
function x(t2) {
  return w && null != t2 && t2 instanceof Uint8Array;
}
function I() {
  return C ||= new N(null, R);
}
function F(t2) {
  O(R);
  var e2 = t2.h;
  return null == (e2 = null == e2 || x(e2) ? e2 : "string" == typeof e2 ? L(e2) : null) ? e2 : t2.h = e2;
}
function O(t2) {
  if (t2 !== R) throw Error("illegal external caller");
}
function M(t2, e2) {
  t2.__closure__error__context__984382 || (t2.__closure__error__context__984382 = {}), t2.__closure__error__context__984382.severity = e2;
}
function P(t2) {
  return M(t2 = Error(t2), "warning"), t2;
}
function $(e2, r2) {
  if (null != e2) {
    var n2 = D ??= {}, i2 = n2[e2] || 0;
    i2 >= r2 || (n2[e2] = i2 + 1, M(e2 = Error(), "incident"), (function(e3) {
      t.setTimeout(() => {
        throw e3;
      }, 0);
    })(e2));
  }
}
function U() {
  return "function" == typeof BigInt;
}
function j(t2, e2, r2 = false) {
  return "function" == typeof Symbol && "symbol" == typeof /* @__PURE__ */ Symbol() ? r2 && Symbol.for && t2 ? Symbol.for(t2) : null != t2 ? Symbol(t2) : /* @__PURE__ */ Symbol() : e2;
}
function rt(t2, e2) {
  B || tt in t2 || Q(t2, Z), t2[tt] |= e2;
}
function nt(t2, e2) {
  B || tt in t2 || Q(t2, Z), t2[tt] = e2;
}
function it(t2) {
  return rt(t2, 34), t2;
}
function st(t2) {
  return rt(t2, 8192), t2;
}
function ot(t2, e2) {
  return 2 & e2 && (t2 |= 2), 1 | t2;
}
function ht(t2) {
  return null != t2 && t2[J] === at;
}
function ut(t2, e2) {
  return void 0 === e2 ? t2.i !== ct && !!(2 & t2.B[tt]) : !!(2 & e2) && t2.i !== ct;
}
function lt(t2, e2) {
  if (null != t2) {
    if ("string" == typeof t2) t2 = t2 ? new N(t2, R) : I();
    else if (!(t2 instanceof N)) if (x(t2)) t2 = t2.length ? new N(new Uint8Array(t2), R) : I();
    else {
      if (!e2) throw Error();
      t2 = void 0;
    }
  }
  return t2;
}
function ft(t2, e2, r2) {
  var n2, i2 = 128 & e2 ? 0 : -1, s2 = t2.length;
  (n2 = !!s2) && (n2 = null != (n2 = t2[s2 - 1]) && "object" == typeof n2 && n2.constructor === Object);
  var o2 = s2 + (n2 ? -1 : 0);
  for (e2 = 128 & e2 ? 1 : 0; e2 < o2; e2++) r2(e2 - i2, t2[e2]);
  if (n2) {
    t2 = t2[s2 - 1];
    for (let e3 in t2) !isNaN(e3) && r2(+e3, t2[e3]);
  }
}
function mt(t2) {
  return 128 & t2 ? pt : void 0;
}
function gt(t2) {
  return t2.hb = true, t2;
}
function At(t2) {
  var e2 = t2;
  if (_t(e2)) {
    if (!/^\s*(?:-?[1-9]\d*|0)?\s*$/.test(e2)) throw Error(String(e2));
  } else if (vt(e2) && !Number.isSafeInteger(e2)) throw Error(String(e2));
  return Et ? BigInt(t2) : t2 = yt(t2) ? t2 ? "1" : "0" : _t(t2) ? t2.trim() || "0" : String(t2);
}
function Lt(t2, e2) {
  if (t2.length > e2.length) return false;
  if (t2.length < e2.length || t2 === e2) return true;
  for (let r2 = 0; r2 < t2.length; r2++) {
    let n2 = t2[r2], i2 = e2[r2];
    if (n2 > i2) return false;
    if (n2 < i2) return true;
  }
}
function Ct(t2) {
  var e2 = t2 >>> 0;
  It = e2, Ft = (t2 - e2) / 4294967296 >>> 0;
}
function Nt(t2) {
  if (t2 < 0) {
    Ct(-t2);
    let [e2, r2] = Gt(It, Ft);
    It = e2 >>> 0, Ft = r2 >>> 0;
  } else Ct(t2);
}
function Ot(t2) {
  var e2 = xt ||= new DataView(new ArrayBuffer(8));
  e2.setFloat32(0, +t2, true), Ft = 0, It = e2.getUint32(0, true);
}
function Mt(t2, e2) {
  var r2 = 4294967296 * e2 + (t2 >>> 0);
  return Number.isSafeInteger(r2) ? r2 : $t(t2, e2);
}
function Dt(t2, e2) {
  return At(U() ? BigInt.asUintN(64, (BigInt(e2 >>> 0) << BigInt(32)) + BigInt(t2 >>> 0)) : $t(t2, e2));
}
function Pt(t2, e2) {
  return U() ? At(BigInt.asIntN(64, (BigInt.asUintN(32, BigInt(e2)) << BigInt(32)) + BigInt.asUintN(32, BigInt(t2)))) : At(Bt(t2, e2));
}
function $t(t2, e2) {
  if (t2 >>>= 0, (e2 >>>= 0) <= 2097151) var r2 = "" + (4294967296 * e2 + t2);
  else U() ? r2 = "" + (BigInt(e2) << BigInt(32) | BigInt(t2)) : (t2 = (16777215 & t2) + 6777216 * (r2 = 16777215 & (t2 >>> 24 | e2 << 8)) + 6710656 * (e2 = e2 >> 16 & 65535), r2 += 8147497 * e2, e2 *= 2, t2 >= 1e7 && (r2 += t2 / 1e7 >>> 0, t2 %= 1e7), r2 >= 1e7 && (e2 += r2 / 1e7 >>> 0, r2 %= 1e7), r2 = e2 + Ut(r2) + Ut(t2));
  return r2;
}
function Ut(t2) {
  return t2 = String(t2), "0000000".slice(t2.length) + t2;
}
function Bt(t2, e2) {
  if (2147483648 & e2) if (U()) t2 = "" + (BigInt(0 | e2) << BigInt(32) | BigInt(t2 >>> 0));
  else {
    let [r2, n2] = Gt(t2, e2);
    t2 = "-" + $t(r2, n2);
  }
  else t2 = $t(t2, e2);
  return t2;
}
function jt(t2) {
  if (t2.length < 16) Nt(Number(t2));
  else if (U()) t2 = BigInt(t2), It = Number(t2 & BigInt(4294967295)) >>> 0, Ft = Number(t2 >> BigInt(32) & BigInt(4294967295));
  else {
    let e2 = +("-" === t2[0]);
    Ft = It = 0;
    let r2 = t2.length;
    for (let n2 = e2, i2 = (r2 - e2) % 6 + e2; i2 <= r2; n2 = i2, i2 += 6) {
      let e3 = Number(t2.slice(n2, i2));
      Ft *= 1e6, (It = 1e6 * It + e3) >= 4294967296 && (Ft += Math.trunc(It / 4294967296), Ft >>>= 0, It >>>= 0);
    }
    if (e2) {
      let [t3, e3] = Gt(It, Ft);
      It = t3, Ft = e3;
    }
  }
}
function Gt(t2, e2) {
  return e2 = ~e2, t2 ? t2 = 1 + ~t2 : e2 += 1, [t2, e2];
}
function Vt(t2) {
  return Array.prototype.slice.call(t2);
}
function qt(t2) {
  if ("number" != typeof t2) throw Error(`Value of float/double field must be a number, found ${typeof t2}: ${t2}`);
  return t2;
}
function Jt(t2) {
  return null == t2 || "number" == typeof t2 ? t2 : "NaN" === t2 || "Infinity" === t2 || "-Infinity" === t2 ? Number(t2) : void 0;
}
function Zt(t2) {
  if ("boolean" != typeof t2) {
    var e2 = typeof t2;
    throw Error(`Expected boolean but got ${"object" != e2 ? e2 : t2 ? Array.isArray(t2) ? "array" : e2 : "null"}: ${t2}`);
  }
  return t2;
}
function te(t2) {
  switch (typeof t2) {
    case "bigint":
      return true;
    case "number":
      return Wt(t2);
    case "string":
      return Qt.test(t2);
    default:
      return false;
  }
}
function ee(t2) {
  if (null != t2) {
    if (!Wt(t2)) throw P("enum");
    t2 |= 0;
  }
  return t2;
}
function re(t2) {
  if (null == t2) return t2;
  if ("string" == typeof t2 && t2) t2 = +t2;
  else if ("number" != typeof t2) return;
  return Wt(t2) ? 0 | t2 : void 0;
}
function ne(t2) {
  if (null == t2) return t2;
  if ("string" == typeof t2 && t2) t2 = +t2;
  else if ("number" != typeof t2) return;
  return Wt(t2) ? t2 >>> 0 : void 0;
}
function ie(t2, e2) {
  if (e2 ??= 1024, !te(t2)) throw P("int64");
  var r2 = typeof t2;
  switch (e2) {
    case 512:
      switch (r2) {
        case "string":
          return ue(t2);
        case "bigint":
          return String(Ht(64, t2));
        default:
          return he(t2);
      }
    case 1024:
      switch (r2) {
        case "string":
          return ce(t2);
        case "bigint":
          return At(Ht(64, t2));
        default:
          return At(Xt(t2) ? t2 : he(t2));
      }
    case 0:
      switch (r2) {
        case "string":
          return ue(t2);
        case "bigint":
          return At(Ht(64, t2));
        default:
          return ae(t2);
      }
    default:
      return (function(t3, e3 = `unexpected value ${t3}!`) {
        throw Error(e3);
      })(e2, "Unknown format requested type for int64");
  }
}
function se(t2) {
  var e2 = t2.length;
  return ("-" === t2[0] ? e2 < 20 || 20 === e2 && t2 <= "-9223372036854775808" : e2 < 19 || 19 === e2 && t2 <= "9223372036854775807") ? t2 : (jt(t2), Bt(It, Ft));
}
function oe(t2) {
  if ("-" === t2[0]) var e2 = false;
  else e2 = (e2 = t2.length) < 20 || 20 === e2 && t2 <= "18446744073709551615";
  return e2 ? t2 : (jt(t2), $t(It, Ft));
}
function ae(t2) {
  if (t2 = Yt(t2), !Xt(t2)) {
    Nt(t2);
    var e2 = It, r2 = Ft;
    (t2 = 2147483648 & r2) && (r2 = ~r2 >>> 0, 0 == (e2 = 1 + ~e2 >>> 0) && (r2 = r2 + 1 >>> 0)), t2 = "number" == typeof (e2 = Mt(e2, r2)) ? t2 ? -e2 : e2 : t2 ? "-" + e2 : e2;
  }
  return t2;
}
function he(t2) {
  return t2 = Yt(t2), Xt(t2) ? t2 = String(t2) : (Nt(t2), t2 = Bt(It, Ft)), t2;
}
function ue(t2) {
  var e2 = Yt(Number(t2));
  return Xt(e2) ? String(e2) : (-1 !== (e2 = t2.indexOf(".")) && (t2 = t2.substring(0, e2)), se(t2));
}
function ce(t2) {
  var e2 = Yt(Number(t2));
  return Xt(e2) ? At(e2) : (-1 !== (e2 = t2.indexOf(".")) && (t2 = t2.substring(0, e2)), U() ? At(Ht(64, BigInt(t2))) : At(se(t2)));
}
function le(t2) {
  var e2 = typeof t2;
  return null == t2 ? t2 : "bigint" === e2 ? At(Ht(64, t2)) : te(t2) ? "string" === e2 ? ce(t2) : At(Xt(t2) ? t2 : he(t2)) : void 0;
}
function de(t2) {
  if (null == t2) return t2;
  var e2 = typeof t2;
  if ("bigint" === e2) return String(Ht(64, t2));
  if (te(t2)) {
    if ("string" === e2) return ue(t2);
    if ("number" === e2) return ae(t2);
  }
}
function fe(t2) {
  if (null == t2) return t2;
  var e2 = typeof t2;
  if ("bigint" === e2) return String(Kt(64, t2));
  if (te(t2)) {
    if ("string" === e2) return e2 = Yt(Number(t2)), Xt(e2) && e2 >= 0 ? t2 = String(e2) : (-1 !== (e2 = t2.indexOf(".")) && (t2 = t2.substring(0, e2)), t2 = oe(t2)), t2;
    if ("number" === e2) return (function(t3) {
      return (t3 = Yt(t3)) >= 0 && Xt(t3) || (Nt(t3), t3 = Mt(It, Ft)), t3;
    })(t2);
  }
}
function pe(t2) {
  if (null == t2 || "string" == typeof t2 || t2 instanceof N) return t2;
}
function me(t2) {
  if ("string" != typeof t2) throw Error();
  return t2;
}
function ge(t2) {
  if (null != t2 && "string" != typeof t2) throw Error();
  return t2;
}
function ve(t2) {
  return null == t2 || "string" == typeof t2 ? t2 : void 0;
}
function _e(t2, e2, r2, n2) {
  return ht(t2) ? t2 : Array.isArray(t2) ? ((n2 = (r2 = 0 | t2[tt]) | 32 & n2 | 2 & n2) !== r2 && nt(t2, n2), new e2(t2)) : (r2 ? 2 & n2 ? ((t2 = e2[H]) || (it((t2 = new e2()).B), t2 = e2[H] = t2), e2 = t2) : e2 = new e2() : e2 = void 0, e2);
}
function ye(t2, e2, r2) {
  return e2 ? ie(t2, 1024) : le(t2) ?? (r2 ? zt : void 0);
}
function Ee(t2) {
  return t2;
}
function ke(t2) {
  return t2;
}
function Se(t2) {
  if (2 & t2.N) throw Error("Cannot mutate an immutable Map");
}
function Re(t2, e2, r2, n2, i2, s2) {
  return t2 = _e(t2, n2, r2, s2), i2 && (t2 = qe(t2)), t2;
}
function Ie(t2) {
  return [t2, this.get(t2)];
}
function Fe() {
  return Le ||= new xe(it([]), void 0, void 0, void 0, Ae);
}
function Ce(t2) {
  return X ? t2[X] : void 0;
}
function Ne(t2, e2) {
  for (let r2 in t2) !isNaN(r2) && e2(t2, +r2, t2[r2]);
}
function $e(t2, e2) {
  e2 < 100 || $(Y, 1);
}
function Ue(t2, e2, r2, n2) {
  var i2 = void 0 !== n2;
  n2 = !!n2;
  var s2, o2 = X;
  !i2 && B && o2 && (s2 = t2[o2]) && Ne(s2, $e), o2 = [];
  var a2 = t2.length;
  s2 = 4294967295;
  var h2 = false, u2 = !!(64 & e2), c2 = u2 ? 128 & e2 ? 0 : -1 : void 0;
  if (!(1 & e2)) {
    var l2 = a2 && t2[a2 - 1];
    null != l2 && "object" == typeof l2 && l2.constructor === Object ? s2 = --a2 : l2 = void 0, !u2 || 128 & e2 || i2 || (h2 = true, s2 = (Oe ?? Ee)(s2 - c2, c2, t2, l2, void 0) + c2);
  }
  e2 = void 0;
  for (var d2 = 0; d2 < a2; d2++) {
    let i3 = t2[d2];
    if (null != i3 && null != (i3 = r2(i3, n2))) if (u2 && d2 >= s2) {
      let t3 = d2 - c2;
      (e2 ??= {})[t3] = i3;
    } else o2[d2] = i3;
  }
  if (l2) for (let t3 in l2) {
    if (null == (a2 = l2[t3]) || null == (a2 = r2(a2, n2))) continue;
    let i3;
    d2 = +t3, u2 && !Number.isNaN(d2) && (i3 = d2 + c2) < s2 ? o2[i3] = a2 : (e2 ??= {})[t3] = a2;
  }
  return e2 && (h2 ? o2.push(e2) : o2[s2] = e2), i2 && (r2 = X) && (t2 = Ce(t2)) && t2 instanceof Pe && (o2[r2] = (function(t3) {
    var e3 = new Pe();
    return Ne(t3, (t4, r3, n3) => {
      e3[r3] = Vt(n3);
    }), e3.h = t3.h, e3;
  })(t2)), o2;
}
function Be(t2) {
  return t2[0] = je(t2[0]), t2[1] = je(t2[1]), t2;
}
function je(t2) {
  switch (typeof t2) {
    case "number":
      return Number.isFinite(t2) ? t2 : "" + t2;
    case "bigint":
      return wt(t2) ? Number(t2) : "" + t2;
    case "boolean":
      return t2 ? 1 : 0;
    case "object":
      if (Array.isArray(t2)) {
        var e2 = 0 | t2[tt];
        return 0 === t2.length && 1 & e2 ? void 0 : Ue(t2, e2, je);
      }
      if (ht(t2)) return Ge(t2);
      if (t2 instanceof N) {
        if (null == (e2 = t2.h)) t2 = "";
        else if ("string" == typeof e2) t2 = e2;
        else {
          if (T) {
            for (var r2 = "", n2 = 0, i2 = e2.length - 10240; n2 < i2; ) r2 += String.fromCharCode.apply(null, e2.subarray(n2, n2 += 10240));
            r2 += String.fromCharCode.apply(null, n2 ? e2.subarray(n2) : e2), e2 = btoa(r2);
          } else {
            void 0 === r2 && (r2 = 0), A(), r2 = _[r2], n2 = Array(Math.floor(e2.length / 3)), i2 = r2[64] || "";
            let t3 = 0, u2 = 0;
            for (; t3 < e2.length - 2; t3 += 3) {
              var s2 = e2[t3], o2 = e2[t3 + 1], a2 = e2[t3 + 2], h2 = r2[s2 >> 2];
              s2 = r2[(3 & s2) << 4 | o2 >> 4], o2 = r2[(15 & o2) << 2 | a2 >> 6], a2 = r2[63 & a2], n2[u2++] = h2 + s2 + o2 + a2;
            }
            switch (h2 = 0, a2 = i2, e2.length - t3) {
              case 2:
                a2 = r2[(15 & (h2 = e2[t3 + 1])) << 2] || i2;
              case 1:
                e2 = e2[t3], n2[u2] = r2[e2 >> 2] + r2[(3 & e2) << 4 | h2 >> 4] + a2 + i2;
            }
            e2 = n2.join("");
          }
          t2 = t2.h = e2;
        }
        return t2;
      }
      return t2 instanceof xe ? t2 = 0 !== t2.size ? t2.fa(Be) : void 0 : void 0;
  }
  return t2;
}
function Ge(t2) {
  return Ue(t2 = t2.B, 0 | t2[tt], je);
}
function Ve(t2, e2) {
  return He(t2, e2[0], e2[1]);
}
function He(t2, e2, r2, n2 = 0) {
  if (null == t2) {
    var i2 = 32;
    r2 ? (t2 = [r2], i2 |= 128) : t2 = [], e2 && (i2 = -16760833 & i2 | (1023 & e2) << 14);
  } else {
    if (!Array.isArray(t2)) throw Error("narr");
    if (i2 = 0 | t2[tt], o && 1 & i2) throw Error("rfarr");
    if (2048 & i2 && !(2 & i2) && (function() {
      if (o) throw Error("carr");
      $(q, 5);
    })(), 256 & i2) throw Error("farr");
    if (64 & i2) return (i2 | n2) !== i2 && nt(t2, i2 | n2), t2;
    if (r2 && (i2 |= 128, r2 !== t2[0])) throw Error("mid");
    t: {
      i2 |= 64;
      var s2 = (r2 = t2).length;
      if (s2) {
        var a2 = s2 - 1;
        let t3 = r2[a2];
        if (null != t3 && "object" == typeof t3 && t3.constructor === Object) {
          if ((a2 -= e2 = 128 & i2 ? 0 : -1) >= 1024) throw Error("pvtlmt");
          for (var h2 in t3) (s2 = +h2) < a2 && (r2[s2 + e2] = t3[h2], delete t3[h2]);
          i2 = -16760833 & i2 | (1023 & a2) << 14;
          break t;
        }
      }
      if (e2) {
        if ((h2 = Math.max(e2, s2 - (128 & i2 ? 0 : -1))) > 1024) throw Error("spvt");
        i2 = -16760833 & i2 | (1023 & h2) << 14;
      }
    }
  }
  return nt(t2, 64 | i2 | n2), t2;
}
function Ke(t2, e2) {
  if ("object" != typeof t2) return t2;
  if (Array.isArray(t2)) {
    var r2 = 0 | t2[tt];
    return 0 === t2.length && 1 & r2 ? void 0 : Xe(t2, r2, e2);
  }
  if (ht(t2)) return Ye(t2);
  if (t2 instanceof xe) {
    if (2 & (e2 = t2.N)) return t2;
    if (!t2.size) return;
    if (r2 = it(t2.fa()), t2.O) for (t2 = 0; t2 < r2.length; t2++) {
      let n2 = r2[t2], i2 = n2[1];
      i2 = null == i2 || "object" != typeof i2 ? void 0 : ht(i2) ? Ye(i2) : Array.isArray(i2) ? Xe(i2, 0 | i2[tt], !!(32 & e2)) : void 0, n2[1] = i2;
    }
    return r2;
  }
  return t2 instanceof N ? t2 : void 0;
}
function Xe(t2, e2, r2) {
  return 2 & e2 || (!r2 || 4096 & e2 || 16 & e2 ? t2 = ze(t2, e2, false, r2 && !(16 & e2)) : (rt(t2, 34), 4 & e2 && Object.freeze(t2))), t2;
}
function We(t2, e2, r2) {
  return t2 = new t2.constructor(e2), r2 && (t2.i = ct), t2.o = ct, t2;
}
function Ye(t2) {
  var e2 = t2.B, r2 = 0 | e2[tt];
  return ut(t2, r2) ? t2 : tr(t2, e2, r2) ? We(t2, e2) : ze(e2, r2);
}
function ze(t2, e2, r2, n2) {
  return n2 ??= !!(34 & e2), t2 = Ue(t2, e2, Ke, n2), n2 = 32, r2 && (n2 |= 2), nt(t2, e2 = 16769217 & e2 | n2), t2;
}
function qe(t2) {
  var e2 = t2.B, r2 = 0 | e2[tt];
  return ut(t2, r2) ? tr(t2, e2, r2) ? We(t2, e2, true) : new t2.constructor(ze(e2, r2, false)) : t2;
}
function Je(t2) {
  if (t2.i !== ct) return false;
  var e2 = t2.B;
  return rt(e2 = ze(e2, 0 | e2[tt]), 2048), t2.B = e2, t2.i = void 0, t2.o = void 0, true;
}
function Ze(t2) {
  if (!Je(t2) && ut(t2, 0 | t2.B[tt])) throw Error();
}
function Qe(t2, e2) {
  void 0 === e2 && (e2 = 0 | t2[tt]), 32 & e2 && !(4096 & e2) && nt(t2, 4096 | e2);
}
function tr(t2, e2, r2) {
  return !!(2 & r2) || !(!(32 & r2) || 4096 & r2) && (nt(e2, 2 | r2), t2.i = ct, true);
}
function nr(t2, e2, r2, n2) {
  if (null !== (e2 = ir(t2.B, e2, void 0, n2)) || r2 && t2.o !== ct) return e2;
}
function ir(t2, e2, r2, n2) {
  if (-1 === e2) return null;
  var i2 = e2 + (r2 ? 0 : -1), s2 = t2.length - 1;
  if (!(s2 < 1 + (r2 ? 0 : -1))) {
    if (i2 >= s2) {
      var o2 = t2[s2];
      if (null != o2 && "object" == typeof o2 && o2.constructor === Object) {
        r2 = o2[e2];
        var a2 = true;
      } else {
        if (i2 !== s2) return;
        r2 = o2;
      }
    } else r2 = t2[i2];
    if (n2 && null != r2) {
      if (null == (n2 = n2(r2))) return n2;
      if (!Object.is(n2, r2)) return a2 ? o2[e2] = n2 : t2[i2] = n2, n2;
    }
    return r2;
  }
}
function sr(t2, e2, r2, n2) {
  Ze(t2);
  var i2 = t2.B;
  return or(i2, 0 | i2[tt], e2, r2, n2), t2;
}
function or(t2, e2, r2, n2, i2) {
  var s2 = r2 + (i2 ? 0 : -1), o2 = t2.length - 1;
  if (o2 >= 1 + (i2 ? 0 : -1) && s2 >= o2) {
    let i3 = t2[o2];
    if (null != i3 && "object" == typeof i3 && i3.constructor === Object) return i3[r2] = n2, e2;
  }
  return s2 <= o2 ? (t2[s2] = n2, e2) : (void 0 !== n2 && (r2 >= (o2 = (e2 ??= 0 | t2[tt]) >> 14 & 1023 || 536870912) ? null != n2 && (t2[o2 + (i2 ? 0 : -1)] = { [r2]: n2 }) : t2[s2] = n2), e2);
}
function ar(t2, e2) {
  return ht(t2 = ir(t2.B, e2)) || Array.isArray(t2);
}
function hr(t2, e2, r2) {
  return ar(t2, e2 = vr(t2, r2) === e2 ? e2 : -1);
}
function ur(t2, e2, r2, n2, i2) {
  var s2 = t2.B, o2 = 0 | s2[tt];
  n2 = n2 && 4 !== n2 ? 1 !== n2 && (t2 ? ut(t2, o2) : 2 & o2) ? 1 : n2 : 4, i2 = !!i2 || 3 === n2, 2 === n2 && Je(t2) && (o2 = 0 | (s2 = t2.B)[tt]);
  var a2 = (t2 = lr(s2, e2)) === G ? 7 : 0 | t2[tt], h2 = ot(a2, o2), u2 = !(4 & h2);
  if (u2) {
    let e3 = 0, n3 = 0;
    for (; e3 < t2.length; e3++) {
      let i3 = r2(t2[e3]);
      null != i3 && (t2[n3++] = i3);
    }
    n3 < e3 && (t2.length = n3), r2 = -513 & h2 | 4, h2 = r2 &= -1025, h2 &= -4097;
  }
  return h2 !== a2 && (nt(t2, h2), 2 & h2 && Object.freeze(t2)), cr(t2, h2, s2, o2, e2, n2, u2, i2);
}
function cr(t2, e2, r2, n2, i2, s2, o2, a2) {
  var h2 = e2, u2 = dr(e2);
  return u2 && 2 === s2 && (t2 = Vt(t2), h2 = 0, e2 = Ir(e2, n2), n2 = or(r2, n2, i2, t2), u2 = false), u2 || (1 === s2 || 4 === s2 && (2 & e2 || !(16 & e2) && 32 & n2) ? ((e2 |= !t2.length || o2 && !(4096 & e2) || 32 & n2 && !(4096 & e2 || 16 & e2) ? 2 : 256) !== h2 && nt(t2, e2), Object.freeze(t2)) : (a2 || (e2 |= 16), e2 !== h2 && nt(t2, e2))), 2 & e2 || !(4096 & e2 || 16 & e2) || Qe(r2, n2), t2;
}
function lr(t2, e2, r2) {
  return t2 = ir(t2, e2, r2), Array.isArray(t2) ? t2 : G;
}
function dr(t2) {
  return !!(2 & t2) && !!(4 & t2) || !!(256 & t2);
}
function fr(t2) {
  return lt(t2, true);
}
function pr(t2) {
  t2 = Vt(t2);
  for (let e2 = 0; e2 < t2.length; e2++) {
    let r2 = t2[e2] = Vt(t2[e2]);
    Array.isArray(r2[1]) && (r2[1] = it(r2[1]));
  }
  return st(t2);
}
function mr(t2, e2, r2, n2) {
  Ze(t2), or(t2 = t2.B, 0 | t2[tt], e2, ("0" === n2 ? 0 === Number(r2) : r2 === n2) ? void 0 : r2);
}
function gr(t2, e2, r2) {
  if (2 & e2) throw Error();
  var n2 = mt(e2), i2 = lr(t2, r2, n2), s2 = i2 === G ? 7 : 0 | i2[tt], o2 = ot(s2, e2);
  return (2 & o2 || dr(o2) || 16 & o2) && (o2 === s2 || dr(o2) || nt(i2, o2), i2 = Vt(i2), s2 = 0, o2 = Ir(o2, e2), or(t2, e2, r2, i2, n2)), (o2 &= -13) !== s2 && nt(i2, o2), i2;
}
function vr(t2, e2) {
  return Er(_r(t2 = t2.B), t2, void 0, e2);
}
function _r(t2) {
  if (B) return t2[K] ?? (t2[K] = /* @__PURE__ */ new Map());
  if (K in t2) return t2[K];
  var e2 = /* @__PURE__ */ new Map();
  return Object.defineProperty(t2, K, { value: e2 }), e2;
}
function yr(t2, e2, r2, n2, i2) {
  var s2 = _r(t2), o2 = Er(s2, t2, e2, r2, i2);
  return o2 !== n2 && (o2 && (e2 = or(t2, e2, o2, void 0, i2)), s2.set(r2, n2)), e2;
}
function Er(t2, e2, r2, n2, i2) {
  var s2 = t2.get(n2);
  if (null != s2) return s2;
  s2 = 0;
  for (let t3 = 0; t3 < n2.length; t3++) {
    let o2 = n2[t3];
    null != ir(e2, o2, i2) && (0 !== s2 && (r2 = or(e2, r2, s2, void 0, i2)), s2 = o2);
  }
  return t2.set(n2, s2), s2;
}
function Ar(t2, e2, r2) {
  var n2 = 0 | t2[tt], i2 = mt(n2), s2 = ir(t2, r2, i2);
  if (ht(s2)) {
    if (!ut(s2)) return Je(s2), s2.B;
    var o2 = s2.B;
  } else Array.isArray(s2) && (o2 = s2, 2 & n2 && it(o2));
  if (o2) {
    let t3 = 0 | o2[tt];
    2 & t3 && (o2 = ze(o2, t3));
  }
  return (o2 = Ve(o2, e2)) !== s2 && or(t2, n2, r2, o2, i2), o2;
}
function wr(t2, e2, r2, n2, i2) {
  var s2 = false;
  if (n2 = (function(t3, e3, r3, n3) {
    return ir(t3, e3, r3, n3);
  })(t2, n2, i2, (t3) => {
    var n3 = _e(t3, r2, false, e2);
    return s2 = n3 !== t3 && null != n3, n3;
  }), null != n2) return s2 && !ut(n2) && Qe(t2, e2), n2;
}
function Tr(t2, e2, r2, n2) {
  var i2 = t2.B, s2 = 0 | i2[tt];
  if (null == (e2 = wr(i2, s2, e2, r2, n2))) return e2;
  if (!ut(t2, s2 = 0 | i2[tt])) {
    let o2 = qe(e2);
    o2 !== e2 && (Je(t2) && (s2 = 0 | (i2 = t2.B)[tt]), Qe(i2, s2 = or(i2, s2, r2, e2 = o2, n2)));
  }
  return e2;
}
function br(t2, e2, r2, n2, i2, s2, o2, a2) {
  s2 = s2 && 4 !== s2 ? 1 !== s2 && (t2 ? ut(t2, r2) : 2 & r2) ? 1 : s2 : 4, o2 = !!o2 || 3 === s2;
  var h2 = !!a2 && !(t2 ? ut(t2, r2) : 2 & r2);
  (2 === s2 || h2) && t2 && Je(t2) && (r2 = 0 | (e2 = t2.B)[tt]);
  var u2 = (t2 = lr(e2, i2)) === G ? 7 : 0 | t2[tt], c2 = ot(u2, r2);
  if (a2 = !(4 & c2)) {
    var l2 = t2, d2 = r2;
    let e3 = !!(2 & c2);
    e3 && (d2 |= 2);
    let i3 = !e3, s3 = true, o3 = 0, a3 = 0;
    for (; o3 < l2.length; o3++) {
      let t3 = _e(l2[o3], n2, false, d2);
      if (t3 instanceof n2) {
        if (!e3) {
          let e4 = ut(t3);
          i3 &&= !e4, s3 &&= e4;
        }
        l2[a3++] = t3;
      }
    }
    a3 < o3 && (l2.length = a3), c2 |= 4, c2 = s3 ? -4097 & c2 : 4096 | c2, c2 = i3 ? 8 | c2 : -9 & c2;
  }
  if (c2 !== u2 && (nt(t2, c2), 2 & c2 && Object.freeze(t2)), h2 && !(8 & c2 || !t2.length && (1 === s2 || 4 === s2 && (2 & c2 || !(16 & c2) && 32 & r2)))) {
    for (dr(c2) && (t2 = Vt(t2), c2 = Ir(c2, r2), r2 = or(e2, r2, i2, t2)), n2 = t2, h2 = c2, u2 = 0; u2 < n2.length; u2++) (l2 = n2[u2]) !== (c2 = qe(l2)) && (n2[u2] = c2);
    h2 |= 8, nt(t2, c2 = h2 = n2.length ? 4096 | h2 : -4097 & h2);
  }
  return cr(t2, c2, e2, r2, i2, s2, a2, o2);
}
function kr(t2, e2, r2) {
  var n2 = t2.B;
  return br(t2, n2, 0 | n2[tt], e2, r2, void 0, false, true);
}
function Sr(t2) {
  return null == t2 && (t2 = void 0), t2;
}
function Lr(t2, e2, r2, n2, i2) {
  return sr(t2, r2, n2 = Sr(n2), i2), n2 && !ut(n2) && Qe(t2.B), t2;
}
function xr(t2, e2, r2, n2) {
  t: {
    var i2 = n2 = Sr(n2);
    Ze(t2);
    let s2 = t2.B, o2 = 0 | s2[tt];
    if (null == i2) {
      let t3 = _r(s2);
      if (Er(t3, s2, o2, r2) !== e2) break t;
      t3.set(r2, 0);
    } else o2 = yr(s2, o2, r2, e2);
    or(s2, o2, e2, i2);
  }
  return n2 && !ut(n2) && Qe(t2.B), t2;
}
function Rr(t2, e2, r2) {
  Ze(t2);
  var n2 = t2.B, i2 = 0 | n2[tt];
  if (null == r2) or(n2, i2, e2);
  else {
    var s2 = r2 === G ? 7 : 0 | r2[tt], o2 = s2, a2 = dr(s2), h2 = a2 || Object.isFrozen(r2), u2 = true, c2 = true;
    for (let t3 = 0; t3 < r2.length; t3++) {
      var l2 = r2[t3];
      a2 || (l2 = ut(l2), u2 &&= !l2, c2 &&= l2);
    }
    a2 || (s2 = u2 ? 13 : 5, s2 = c2 ? -4097 & s2 : 4096 | s2), h2 && s2 === o2 || (r2 = Vt(r2), o2 = 0, s2 = Ir(s2, i2)), s2 !== o2 && nt(r2, s2), i2 = or(n2, i2, e2, r2), 2 & s2 || !(4096 & s2 || 16 & s2) || Qe(n2, i2);
  }
  return t2;
}
function Ir(t2, e2) {
  return -273 & (2 & e2 ? 2 | t2 : -3 & t2);
}
function Fr(t2, e2, r2, n2) {
  Ze(t2);
  var i2 = t2.B;
  t2 = br(t2, i2, 0 | i2[tt], r2, e2, 2, true), n2 = null != n2 ? n2 : new r2(), t2.push(n2), e2 = r2 = t2 === G ? 7 : 0 | t2[tt], (n2 = ut(n2)) ? (r2 &= -9, 1 === t2.length && (r2 &= -4097)) : r2 |= 4096, r2 !== e2 && nt(t2, r2), n2 || Qe(i2);
}
function Cr(t2, e2, r2) {
  return re(nr(t2, e2, r2));
}
function Nr(t2, e2) {
  return nr(t2, e2, void 0, Jt) ?? 0;
}
function Or(t2, e2, r2, n2) {
  return Tr(t2, e2, r2 = vr(t2, n2) === r2 ? r2 : -1, void 0);
}
function Mr(t2, e2) {
  mr(t2, 3, null == e2 ? e2 : Zt(e2), false);
}
function Dr(t2, e2, r2) {
  if (null != r2) {
    if ("number" != typeof r2) throw P("int32");
    if (!Wt(r2)) throw P("int32");
    r2 |= 0;
  }
  sr(t2, e2, r2);
}
function Pr(t2, e2, r2) {
  return sr(t2, e2, null == r2 ? r2 : ie(r2));
}
function $r(t2, e2, r2) {
  return sr(t2, e2, null == r2 ? r2 : (function(t3) {
    if (!te(t3)) throw P("uint64");
    switch (typeof t3) {
      case "string":
        var e3 = Yt(Number(t3));
        return Xt(e3) && e3 >= 0 ? t3 = At(e3) : (-1 !== (e3 = t3.indexOf(".")) && (t3 = t3.substring(0, e3)), t3 = U() ? At(Kt(64, BigInt(t3))) : At(oe(t3))), t3;
      case "bigint":
        return At(Kt(64, t3));
      default:
        return t3 >= 0 && Xt(t3) || ((t3 = Yt(t3)) >= 0 && Xt(t3) ? t3 = String(t3) : (Nt(t3), t3 = $t(It, Ft))), At(t3);
    }
  })(r2));
}
function Ur(t2, e2, r2) {
  sr(t2, e2, null == r2 ? r2 : qt(r2));
}
function Br(t2, e2, r2) {
  mr(t2, e2, null == r2 ? r2 : qt(r2), 0);
}
function jr(t2, e2, r2) {
  mr(t2, e2, ge(r2), "");
}
function Gr(t2, e2, r2) {
  {
    Ze(t2);
    let o2 = t2.B, a2 = 0 | o2[tt];
    if (null == r2) or(o2, a2, e2);
    else {
      var n2 = t2 = r2 === G ? 7 : 0 | r2[tt], i2 = dr(t2), s2 = i2 || Object.isFrozen(r2);
      for (i2 || (t2 = 0), s2 || (r2 = Vt(r2), n2 = 0, t2 = Ir(t2, a2), s2 = false), t2 |= 5, t2 |= (4 & t2 ? 512 & t2 ? 512 : 1024 & t2 ? 1024 : 0 : void 0) ?? 1024, i2 = 0; i2 < r2.length; i2++) {
        let e3 = r2[i2], o3 = me(e3);
        Object.is(e3, o3) || (s2 && (r2 = Vt(r2), n2 = 0, t2 = Ir(t2, a2), s2 = false), r2[i2] = o3);
      }
      t2 !== n2 && (s2 && (r2 = Vt(r2), t2 = Ir(t2, a2)), nt(r2, t2)), or(o2, a2, e2, r2);
    }
  }
}
function Vr(t2, e2, r2) {
  Ze(t2), ur(t2, e2, ve, 2, true).push(me(r2));
}
function Kr(t2, e2) {
  if ("string" == typeof t2) return new Hr(L(t2), e2);
  if (Array.isArray(t2)) return new Hr(new Uint8Array(t2), e2);
  if (t2.constructor === Uint8Array) return new Hr(t2, false);
  if (t2.constructor === ArrayBuffer) return t2 = new Uint8Array(t2), new Hr(t2, false);
  if (t2.constructor === N) return e2 = F(t2) || new Uint8Array(0), new Hr(e2, true, t2);
  if (t2 instanceof Uint8Array) return t2 = t2.constructor === Uint8Array ? t2 : new Uint8Array(t2.buffer, t2.byteOffset, t2.byteLength), new Hr(t2, false);
  throw Error();
}
function Xr(t2, e2) {
  var r2 = 0, n2 = 0, i2 = 0, s2 = t2.i, o2 = t2.h;
  do {
    var a2 = s2[o2++];
    r2 |= (127 & a2) << i2, i2 += 7;
  } while (i2 < 32 && 128 & a2);
  if (i2 > 32) for (n2 |= (127 & a2) >> 4, i2 = 3; i2 < 32 && 128 & a2; i2 += 7) n2 |= (127 & (a2 = s2[o2++])) << i2;
  if (tn(t2, o2), !(128 & a2)) return e2(r2 >>> 0, n2 >>> 0);
  throw Error();
}
function Wr(t2) {
  for (var e2 = 0, r2 = t2.h, n2 = r2 + 10, i2 = t2.i; r2 < n2; ) {
    let n3 = i2[r2++];
    if (e2 |= n3, !(128 & n3)) return tn(t2, r2), !!(127 & e2);
  }
  throw Error();
}
function Yr(t2) {
  var e2 = t2.i, r2 = t2.h, n2 = e2[r2++], i2 = 127 & n2;
  if (128 & n2 && (i2 |= (127 & (n2 = e2[r2++])) << 7, 128 & n2 && (i2 |= (127 & (n2 = e2[r2++])) << 14, 128 & n2 && (i2 |= (127 & (n2 = e2[r2++])) << 21, 128 & n2 && (i2 |= (n2 = e2[r2++]) << 28, 128 & n2 && 128 & e2[r2++] && 128 & e2[r2++] && 128 & e2[r2++] && 128 & e2[r2++] && 128 & e2[r2++]))))) throw Error();
  return tn(t2, r2), i2;
}
function zr(t2) {
  return Yr(t2) >>> 0;
}
function qr(t2) {
  return Xr(t2, Pt);
}
function Jr(t2) {
  var e2 = t2.i, r2 = t2.h, n2 = e2[r2], i2 = e2[r2 + 1], s2 = e2[r2 + 2];
  return e2 = e2[r2 + 3], tn(t2, t2.h + 4), (n2 | i2 << 8 | s2 << 16 | e2 << 24) >>> 0;
}
function Zr(t2) {
  var e2 = Jr(t2);
  t2 = 2 * (e2 >> 31) + 1;
  var r2 = e2 >>> 23 & 255;
  return e2 &= 8388607, 255 == r2 ? e2 ? NaN : t2 * (1 / 0) : 0 == r2 ? 1401298464324817e-60 * t2 * e2 : t2 * Math.pow(2, r2 - 150) * (e2 + 8388608);
}
function Qr(t2) {
  return Yr(t2);
}
function tn(t2, e2) {
  if (t2.h = e2, e2 > t2.l) throw Error();
}
function en(t2, e2) {
  if (e2 < 0) throw Error();
  var r2 = t2.h;
  if ((e2 = r2 + e2) > t2.l) throw Error();
  return t2.h = e2, r2;
}
function rn(t2, e2) {
  if (0 == e2) return I();
  var r2 = en(t2, e2);
  return t2.ga && t2.u ? r2 = t2.i.subarray(r2, r2 + e2) : (t2 = t2.i, r2 = r2 === (e2 = r2 + e2) ? new Uint8Array(0) : Rt ? t2.slice(r2, e2) : new Uint8Array(t2.subarray(r2, e2))), 0 == r2.length ? I() : new N(r2, R);
}
function an(t2, e2, r2, n2) {
  if (vn.length) {
    let i2 = vn.pop();
    return i2.A(n2), i2.h.init(t2, e2, r2, n2), i2;
  }
  return new gn(t2, e2, r2, n2);
}
function hn(t2) {
  t2.h.clear(), t2.l = -1, t2.i = -1, vn.length < 100 && vn.push(t2);
}
function un(t2) {
  var e2 = t2.h;
  if (e2.h == e2.l) return false;
  t2.o = t2.h.h;
  var r2 = zr(t2.h);
  if (e2 = r2 >>> 3, !((r2 &= 7) >= 0 && r2 <= 5)) throw Error();
  if (e2 < 1) throw Error();
  return t2.l = e2, t2.i = r2, true;
}
function cn(t2) {
  try {
    switch (t2.i) {
      case 0:
        0 != t2.i ? cn(t2) : Wr(t2.h);
        break;
      case 1:
        var e2 = t2.h;
        tn(e2, e2.h + 8);
        break;
      case 2:
        if (2 != t2.i) cn(t2);
        else {
          var r2 = zr(t2.h), n2 = t2.h;
          tn(n2, n2.h + r2);
        }
        break;
      case 5:
        var i2 = t2.h;
        tn(i2, i2.h + 4);
        break;
      case 3:
        ln();
        let s2 = t2.l;
        try {
          for (; ; ) {
            if (!un(t2)) throw Error();
            if (4 == t2.i) {
              if (t2.l != s2) throw Error();
              break;
            }
            cn(t2);
          }
        } catch (t3) {
          if (t3 instanceof RangeError) throw new SyntaxError();
          throw t3;
        } finally {
          on > 0 && on--;
        }
        break;
      default:
        throw Error();
    }
  } catch (t3) {
    if (t3 instanceof RangeError) throw new SyntaxError();
    throw t3;
  }
}
function ln() {
  if (on >= 100) throw new SyntaxError();
  on++;
}
function dn(t2, e2, r2) {
  var n2 = t2.h.l, i2 = zr(t2.h), s2 = (i2 = t2.h.h + i2) - n2;
  if (s2 <= 0 && (t2.h.l = i2, r2(e2, t2, void 0, void 0, void 0), s2 = i2 - t2.h.h), s2) throw Error();
  return t2.h.h = i2, t2.h.l = n2, e2;
}
function fn(t2) {
  var e2 = zr(t2.h), s2 = en(t2 = t2.h, e2);
  if (t2 = t2.i, u) {
    var o2, a2 = t2;
    (o2 = i) || (o2 = i = new TextDecoder("utf-8", { fatal: true })), e2 = s2 + e2, a2 = 0 === s2 && e2 === a2.length ? a2 : a2.subarray(s2, e2);
    try {
      var c2 = o2.decode(a2);
    } catch (t3) {
      if (void 0 === h) {
        try {
          o2.decode(new Uint8Array([128]));
        } catch (t4) {
        }
        try {
          o2.decode(new Uint8Array([97])), h = true;
        } catch (t4) {
          h = false;
        }
      }
      throw !h && (i = void 0), t3;
    }
  } else {
    e2 = (c2 = s2) + e2, s2 = [];
    let i2, h2 = null;
    for (; c2 < e2; ) {
      var l2 = t2[c2++];
      l2 < 128 ? s2.push(l2) : l2 < 224 ? c2 >= e2 ? r() : (i2 = t2[c2++], l2 < 194 || 128 != (192 & i2) ? (c2--, r()) : s2.push((31 & l2) << 6 | 63 & i2)) : l2 < 240 ? c2 >= e2 - 1 ? r() : (i2 = t2[c2++], 128 != (192 & i2) || 224 === l2 && i2 < 160 || 237 === l2 && i2 >= 160 || 128 != (192 & (o2 = t2[c2++])) ? (c2--, r()) : s2.push((15 & l2) << 12 | (63 & i2) << 6 | 63 & o2)) : l2 <= 244 ? c2 >= e2 - 2 ? r() : (i2 = t2[c2++], 128 != (192 & i2) || i2 - 144 + (l2 << 28) >> 30 || 128 != (192 & (o2 = t2[c2++])) || 128 != (192 & (a2 = t2[c2++])) ? (c2--, r()) : (l2 = (7 & l2) << 18 | (63 & i2) << 12 | (63 & o2) << 6 | 63 & a2, l2 -= 65536, s2.push(55296 + (l2 >> 10 & 1023), 56320 + (1023 & l2)))) : r(), s2.length >= 8192 && (h2 = n(h2, s2), s2.length = 0);
    }
    c2 = n(h2, s2);
  }
  return c2;
}
function pn(t2) {
  var e2 = zr(t2.h);
  return rn(t2.h, e2);
}
function mn(t2, e2, r2) {
  var n2 = zr(t2.h);
  for (n2 = t2.h.h + n2; t2.h.h < n2; ) r2.push(e2(t2.h));
}
function _n(t2) {
  return new An(4294967295 & t2, Math.floor(t2 / 4294967296));
}
function yn(t2) {
  return t2 ? /^\d+$/.test(t2) ? (jt(t2), new An(It, Ft)) : null : En ||= new An(0, 0);
}
function wn(t2) {
  return new Cn(4294967295 & t2, Math.floor(t2 / 4294967296));
}
function Tn(t2) {
  return t2 ? /^-?\d+$/.test(t2) ? (jt(t2), new Cn(It, Ft)) : null : bn ||= new Cn(0, 0);
}
function Nn(t2, e2, r2) {
  return "undefined" != typeof BigInt64Array ? (In || (In = new BigInt64Array(1), Fn = new Uint32Array(In.buffer), In[0] = BigInt(1), Rn = 1 === Fn[0]), In[0] = t2, new e2(Fn[t2 = Rn ? 0 : 1], Fn[1 - t2])) : (xn || (kn = BigInt(Number.MIN_SAFE_INTEGER), Sn = BigInt(Number.MAX_SAFE_INTEGER), Ln = BigInt(4294967295), xn = BigInt(32)), t2 >= kn && t2 <= Sn ? r2(Number(t2)) : (t2 = BigInt.asUintN(64, t2), new e2(Number(t2 & Ln), Number(t2 >> xn))));
}
function On(t2, e2, r2) {
  for (; r2 > 0 || e2 > 127; ) t2.h.push(127 & e2 | 128), e2 = (e2 >>> 7 | r2 << 25) >>> 0, r2 >>>= 7;
  t2.h.push(e2);
}
function Mn(t2, e2) {
  for (; e2 > 127; ) t2.h.push(127 & e2 | 128), e2 >>>= 7;
  t2.h.push(e2);
}
function Dn(t2, e2) {
  if (e2 >= 0) Mn(t2, e2);
  else {
    for (let r2 = 0; r2 < 9; r2++) t2.h.push(127 & e2 | 128), e2 >>= 7;
    t2.h.push(1);
  }
}
function Pn(t2, e2) {
  jt(e2), (function(t3) {
    var e3 = Ft >> 31;
    t3(It << 1 ^ e3, (Ft << 1 | It >>> 31) ^ e3);
  })((e3, r2) => {
    On(t2, e3 >>> 0, r2 >>> 0);
  });
}
function $n(t2, e2) {
  t2.h.push(e2 >>> 0 & 255), t2.h.push(e2 >>> 8 & 255), t2.h.push(e2 >>> 16 & 255), t2.h.push(e2 >>> 24 & 255);
}
function Bn(t2, e2) {
  0 !== e2.length && (t2.l.push(e2), t2.i += e2.length);
}
function jn(t2, e2, r2) {
  Mn(t2.h, 8 * e2 + r2);
}
function Gn(t2, e2) {
  return jn(t2, e2, 2), e2 = t2.h.end(), Bn(t2, e2), e2.push(t2.i), e2;
}
function Vn(t2, e2) {
  var r2 = e2.pop();
  for (r2 = t2.i + t2.h.length() - r2; r2 > 127; ) e2.push(127 & r2 | 128), r2 >>>= 7, t2.i++;
  e2.push(r2), t2.i++;
}
function Hn(t2, e2, r2) {
  if (null != r2) switch (jn(t2, e2, 0), typeof r2) {
    case "number":
      t2 = t2.h, Nt(r2), On(t2, It, Ft);
      break;
    case "bigint":
      r2 = Nn(r2, Cn, wn), On(t2.h, r2.i, r2.h);
      break;
    default:
      r2 = Tn(r2), On(t2.h, r2.i, r2.h);
  }
}
function Kn(t2, e2, r2) {
  jn(t2, e2, 2), Mn(t2.h, r2.length), Bn(t2, t2.h.end()), Bn(t2, r2);
}
function Xn(t2, e2, r2, n2) {
  null != r2 && (e2 = Gn(t2, e2), n2(r2, t2), Vn(t2, e2));
}
function Yn(t2) {
  if ("string" == typeof t2) Tn(t2);
}
function zn() {
  var t2 = class {
    constructor() {
      throw Error();
    }
  };
  return Object.setPrototypeOf(t2, t2.prototype), t2;
}
function hi(t2, e2, r2) {
  var n2 = t2.B;
  X && X in n2 && (n2 = n2[X]) && delete n2[e2.h], e2.i ? e2.u(t2, e2.i, e2.h, r2, e2.l) : e2.u(t2, e2.h, r2, e2.l);
}
function li(t2, e2) {
  return new ci(t2, e2, qn);
}
function di(t2, e2, r2, n2, i2) {
  Xn(t2, r2, bi(e2, n2), i2);
}
function wi(t2, e2, r2, n2) {
  var i2 = n2[t2];
  if (i2) return i2;
  (i2 = {}).Ea = n2, i2.da = (function(t3) {
    switch (typeof t3) {
      case "boolean":
        return Me ||= [0, void 0, true];
      case "number":
        return t3 > 0 ? void 0 : 0 === t3 ? De ||= [0, void 0] : [-t3, void 0];
      case "string":
        return [0, t3];
      case "object":
        return t3;
    }
  })(n2[0]);
  var s2 = n2[1], o2 = 1;
  s2 && s2.constructor === Object && (i2.ja = s2, "function" == typeof (s2 = n2[++o2]) && s2.length > 0 && (i2.wa = true, fi ??= s2, pi ??= n2[o2 + 1], s2 = n2[o2 += 2]));
  for (var a2 = {}; s2 && Array.isArray(s2) && s2.length && "number" == typeof s2[0] && s2[0] > 0; ) {
    for (var h2 = 0; h2 < s2.length; h2++) a2[s2[h2]] = s2;
    s2 = n2[++o2];
  }
  for (h2 = 1; void 0 !== s2; ) {
    let t3;
    "number" == typeof s2 && (h2 += s2, s2 = n2[++o2]);
    var u2 = void 0;
    if (s2 instanceof ci ? t3 = s2 : (t3 = mi, o2--), t3?.l) {
      s2 = n2[++o2], u2 = n2;
      var c2 = o2;
      "function" == typeof s2 && (s2 = s2(), u2[c2] = s2), u2 = s2;
    }
    for (c2 = h2 + 1, "number" == typeof (s2 = n2[++o2]) && s2 < 0 && (c2 -= s2, s2 = n2[++o2]); h2 < c2; h2++) {
      let n3 = a2[h2];
      u2 ? r2(i2, h2, t3, u2, n3) : e2(i2, h2, t3, n3);
    }
  }
  return n2[t2] = i2;
}
function Ti(t2) {
  return Array.isArray(t2) ? t2[0] instanceof ci ? t2 : [gi, t2] : [t2, void 0];
}
function bi(t2, e2) {
  return t2 instanceof ui ? t2.B : Array.isArray(t2) ? Ve(t2, e2) : void 0;
}
function ki(t2, e2, r2, n2) {
  var i2 = r2.h;
  t2[e2] = n2 ? (t3, e3, r3) => i2(t3, e3, r3, n2) : i2;
}
function Si(t2, e2, r2, n2, i2) {
  var s2, o2, a2 = r2.h;
  t2[e2] = (t3, e3, r3) => a2(t3, e3, r3, o2 ||= wi(_i, ki, Si, n2).da, s2 ||= Li(n2), i2);
}
function Li(t2) {
  var e2 = t2[yi];
  if (null != e2) return e2;
  var r2 = wi(_i, ki, Si, t2);
  return e2 = r2.wa ? (t3, e3) => fi(t3, e3, r2) : (t3, e3) => {
    t: {
      ln();
      try {
        for (; un(e3) && 4 != e3.i; ) {
          let h3 = e3.l, u3 = r2[h3];
          if (null == u3) {
            let t4 = r2.ja;
            if (t4) {
              let e4 = t4[h3];
              if (e4) {
                let t5 = Ri(e4);
                null != t5 && (u3 = r2[h3] = t5);
              }
            }
          }
          if (null == u3 || !u3(e3, t3, h3)) {
            var n2 = e3;
            let r3 = n2.o;
            if (cn(n2), n2.ra) var i2 = void 0;
            else {
              let t4 = n2.h.h - r3;
              n2.h.h = r3, i2 = rn(n2.h, t4);
            }
            n2 = void 0;
            var s2 = t3, o2 = h3, a2 = i2;
            a2 && ((n2 = s2[X] ?? (s2[X] = new Pe()))[o2] ?? (n2[o2] = [])).push(a2);
          }
        }
        let u2 = Ce(t3);
        u2 && (u2.h = r2.Ea[Ai]);
        var h2 = true;
        break t;
      } catch (t4) {
        if (t4 instanceof RangeError) throw new SyntaxError();
        throw t4;
      } finally {
        on > 0 && on--;
      }
      h2 = void 0;
    }
    return h2;
  }, t2[yi] = e2, t2[Ai] = xi.bind(t2), e2;
}
function xi(t2, e2, r2, n2) {
  var i2 = this[_i], s2 = this[yi], o2 = Ve(void 0, i2.da), a2 = Ce(t2);
  if (a2) {
    var h2 = false, u2 = i2.ja;
    if (u2) {
      if (i2 = (e3, r3, i3) => {
        if (0 !== i3.length) if (u2[r3]) for (let t3 of i3) {
          e3 = an(t3);
          try {
            h2 = true, s2(o2, e3);
          } finally {
            hn(e3);
          }
        }
        else n2?.(t2, r3, i3);
      }, null == e2) Ne(a2, i2);
      else if (null != a2) {
        let t3 = a2[e2];
        t3 && i2(a2, e2, t3);
      }
      if (h2) {
        let n3 = 0 | t2[tt];
        if (2 & n3 && 2048 & n3 && !r2?.lb) throw Error();
        let i3 = mt(n3), s3 = (e3, s4) => {
          if (null != ir(t2, e3, i3)) {
            if (1 === r2?.kb) return;
            throw Error();
          }
          null != s4 && (n3 = or(t2, n3, e3, s4, i3)), delete a2[e3];
        };
        null == e2 ? ft(o2, 0 | o2[tt], (t3, e3) => {
          s3(t3, e3);
        }) : s3(e2, ir(o2, e2, i3));
      }
    }
  }
}
function Ri(t2) {
  var e2 = (t2 = Ti(t2))[0].h;
  if (t2 = t2[1]) {
    let r2 = Li(t2), n2 = wi(_i, ki, Si, t2).da;
    return (t3, i2, s2) => e2(t3, i2, s2, n2, r2);
  }
  return e2;
}
function Ii(t2, e2, r2) {
  t2[e2] = r2.i;
}
function Fi(t2, e2, r2, n2) {
  var i2, s2, o2 = r2.i;
  t2[e2] = (t3, e3, r3) => o2(t3, e3, r3, s2 ||= wi(vi, Ii, Fi, n2).da, i2 ||= Ci(n2));
}
function Ci(t2) {
  var e2 = t2[Ei];
  if (!e2) {
    let r2 = wi(vi, Ii, Fi, t2);
    e2 = (t3, e3) => Ni(t3, e3, r2), t2[Ei] = e2;
  }
  return e2;
}
function Ni(t2, e2, r2) {
  ft(t2, 0 | t2[tt], (t3, n2) => {
    if (null != n2) {
      var i2 = (function(t4, e3) {
        var r3 = t4[e3];
        if (r3) return r3;
        if ((r3 = t4.ja) && (r3 = r3[e3])) {
          var n3 = (r3 = Ti(r3))[0].i;
          if (r3 = r3[1]) {
            let e4 = Ci(r3), i3 = wi(vi, Ii, Fi, r3).da;
            r3 = t4.wa ? pi(i3, e4) : (t5, r4, s2) => n3(t5, r4, s2, i3, e4);
          } else r3 = n3;
          return t4[e3] = r3;
        }
      })(r2, t3);
      i2 ? i2(e2, n2, t3) : t3 < 500 || $(z, 3);
    }
  }), (t2 = Ce(t2)) && Ne(t2, (t3, r3, n2) => {
    for (Bn(e2, e2.h.end()), t3 = 0; t3 < n2.length; t3++) Bn(e2, F(n2[t3]) || new Uint8Array(0));
  });
}
function Mi(t2, e2, r2) {
  if (Array.isArray(e2)) {
    var n2 = 0 | e2[tt];
    if (4 & n2) return e2;
    for (var i2 = 0, s2 = 0; i2 < e2.length; i2++) {
      let r3 = t2(e2[i2]);
      null != r3 && (e2[s2++] = r3);
    }
    return s2 < i2 && (e2.length = s2), t2 = 1 | n2, r2 && (t2 = -1537 & t2 | 4), t2 !== n2 && nt(e2, t2), r2 && 2 & t2 && Object.freeze(e2), e2;
  }
}
function Pi(t2, e2, r2) {
  return new ci(t2, e2, r2);
}
function $i(t2, e2, r2) {
  return new ci(t2, e2, r2);
}
function Ui(t2, e2, r2) {
  or(t2, 0 | t2[tt], e2, r2, mt(0 | t2[tt]));
}
function ji(t2, e2, r2) {
  null != (e2 = Jt(e2)) && (jn(t2, r2, 5), t2 = t2.h, Ot(e2), $n(t2, It));
}
function Gi(t2, e2, r2) {
  null != (e2 = de(e2)) && (Yn(e2), Hn(t2, r2, e2));
}
function Vi(t2, e2, r2) {
  null != (e2 = re(e2)) && null != e2 && (jn(t2, r2, 0), Dn(t2.h, e2));
}
function Hi(t2, e2, r2) {
  null != (e2 = null == e2 || "boolean" == typeof e2 ? e2 : "number" == typeof e2 ? !!e2 : void 0) && (jn(t2, r2, 0), t2.h.h.push(e2 ? 1 : 0));
}
function Ki(t2, e2, r2) {
  null != (e2 = ve(e2)) && Kn(t2, r2, l(e2));
}
function Xi(t2, e2, r2, n2, i2) {
  Xn(t2, r2, bi(e2, n2), i2);
}
function Wi(t2, e2, r2) {
  null != (e2 = pe(e2)) && Kn(t2, r2, Kr(e2, true).buffer);
}
function Yi(t2, e2, r2) {
  null != (e2 = ne(e2)) && null != e2 && (jn(t2, r2, 0), Mn(t2.h, e2));
}
function zi(t2, e2, r2) {
  null != (e2 = re(e2)) && (e2 = parseInt(e2, 10), jn(t2, r2, 0), Dn(t2.h, e2));
}
function qi(t2, e2, r2) {
  return (5 === t2.i || 2 === t2.i) && (e2 = gr(e2, 0 | e2[tt], r2), 2 == t2.i ? mn(t2, Zr, e2) : e2.push(Zr(t2.h)), true);
}
function Ji(t2, e2, r2) {
  return 0 === t2.i && (Ui(e2, r2, qr(t2.h)), true);
}
function Zi(t2, e2, r2) {
  return (0 === t2.i || 2 === t2.i) && (e2 = gr(e2, 0 | e2[tt], r2), 2 == t2.i ? mn(t2, Yr, e2) : e2.push(Yr(t2.h)), true);
}
function Qi(t2, e2, r2) {
  return 2 === t2.i && (Ui(e2, r2, (t2 = pn(t2)) === I() ? void 0 : t2), true);
}
function Is(t2, e2) {
  return new Rs(t2, e2);
}
function Fs(t2, e2) {
  return (r2, n2) => {
    t: {
      let s2 = { ma: true };
      n2 && Object.assign(s2, n2), r2 = an(r2, void 0, void 0, s2);
      try {
        let n3 = new t2(), s3 = n3.B;
        Li(e2)(s3, r2);
        var i2 = n3;
        break t;
      } catch (t3) {
        if (t3 instanceof RangeError) throw new SyntaxError();
        throw t3;
      } finally {
        hn(r2);
      }
      i2 = void 0;
    }
    return i2;
  };
}
function Cs(t2) {
  return (e2) => Di(e2, t2);
}
function Ns(t2) {
  return function() {
    return Di(this, t2);
  };
}
function Gs(t2) {
  var e2;
  return void 0 === Ds && (Ds = (function() {
    var t3 = null;
    if (!Bs) return t3;
    try {
      let e3 = (t4) => t4;
      t3 = Bs.createPolicy("goog#html", { createHTML: e3, createScript: e3, createScriptURL: e3 });
    } catch (t4) {
    }
    return t3;
  })()), t2 = (e2 = Ds) ? e2.createScriptURL(t2) : t2, new js(t2);
}
function Vs(t2, ...e2) {
  if (0 === e2.length) return Gs(t2[0]);
  var r2 = t2[0];
  for (let n2 = 0; n2 < e2.length; n2++) r2 += encodeURIComponent(e2[n2]) + t2[n2 + 1];
  return Gs(r2);
}
function oo(t2, e2) {
  Vr(t2, 3, e2);
}
function ao(t2, e2) {
  Vr(t2, 4, e2);
}
function fo(t2, e2) {
  Fr(t2, 1, ho, e2);
}
function po(t2, e2) {
  Vr(t2, 10, e2);
}
function mo(t2, e2) {
  Vr(t2, 15, e2);
}
function Ya(t2, e2) {
  return e2 = e2 ? e2.clone() : new jo(), void 0 !== t2.displayNamesLocale ? sr(e2, 1, ge(t2.displayNamesLocale)) : void 0 === t2.displayNamesLocale && sr(e2, 1), void 0 !== t2.maxResults ? Dr(e2, 2, t2.maxResults) : "maxResults" in t2 && sr(e2, 2), void 0 !== t2.scoreThreshold ? Ur(e2, 3, t2.scoreThreshold) : "scoreThreshold" in t2 && sr(e2, 3), void 0 !== t2.categoryAllowlist ? Gr(e2, 4, t2.categoryAllowlist) : "categoryAllowlist" in t2 && sr(e2, 4), void 0 !== t2.categoryDenylist ? Gr(e2, 5, t2.categoryDenylist) : "categoryDenylist" in t2 && sr(e2, 5), e2;
}
function za(t2) {
  var e2 = Number(t2);
  return Number.isSafeInteger(e2) ? e2 : String(t2);
}
function qa(t2, e2 = -1, r2 = "") {
  return { categories: t2.map((t3) => ({ index: Cr(t3, 1) ?? 0 ?? -1, score: Nr(t3, 2) ?? 0, categoryName: ve(nr(t3, 3)) ?? "" ?? "", displayName: ve(nr(t3, 4)) ?? "" ?? "" })), headIndex: e2, headName: r2 };
}
function Ja(t2) {
  var e2 = { classifications: kr(t2, Oo, 1).map((t3) => qa(Tr(t3, Eo, 4)?.h() ?? [], Cr(t3, 2) ?? 0, ve(nr(t3, 3)) ?? "")) };
  return null != (function(t3) {
    return null == t3 ? t3 : "bigint" == typeof t3 ? (wt(t3) ? t3 = Number(t3) : (t3 = Ht(64, t3), t3 = wt(t3) ? Number(t3) : String(t3)), t3) : te(t3) ? "number" == typeof t3 ? ae(t3) : ue(t3) : void 0;
  })(nr(t2, 2, void 0, le)) && (e2.timestampMs = za(nr(t2, 2, void 0, le) ?? er)), e2;
}
function Za(t2) {
  var e2 = ur(t2, 3, Jt), r2 = ur(t2, 2, re), n2 = ur(t2, 1, ve), i2 = ur(t2, 9, ve), s2 = { categories: [], keypoints: [] };
  for (let t3 = 0; t3 < e2.length; t3++) s2.categories.push({ score: e2[t3], index: r2[t3] ?? -1, categoryName: n2[t3] ?? "", displayName: i2[t3] ?? "" });
  if ((e2 = Tr(t2, ko, 4)?.l()) && (s2.boundingBox = { originX: Cr(e2, 1, rr) ?? 0, originY: Cr(e2, 2, rr) ?? 0, width: Cr(e2, 3, rr) ?? 0, height: Cr(e2, 4, rr) ?? 0, angle: 0 }), Tr(t2, ko, 4)?.h().length) for (let e3 of Tr(t2, ko, 4).h()) s2.keypoints.push({ x: nr(e3, 1, rr, Jt) ?? 0, y: nr(e3, 2, rr, Jt) ?? 0, score: nr(e3, 4, rr, Jt) ?? 0, label: ve(nr(e3, 3, rr)) ?? "" });
  return s2;
}
function Qa(t2) {
  var e2 = [];
  for (let r2 of kr(t2, Ro, 1)) t2 = { x: Nr(r2, 1) ?? 0, y: Nr(r2, 2) ?? 0, z: Nr(r2, 3) ?? 0, visibility: Nr(r2, 4) ?? 0 }, null != nr(r2, 5, void 0, Jt) && (t2.presence = Nr(r2, 5) ?? 0), e2.push(t2);
  return e2;
}
function th(t2) {
  var e2 = [];
  for (let r2 of kr(t2, Lo, 1)) t2 = { x: Nr(r2, 1) ?? 0, y: Nr(r2, 2) ?? 0, z: Nr(r2, 3) ?? 0, visibility: Nr(r2, 4) ?? 0 }, null != nr(r2, 5, void 0, Jt) && (t2.presence = Nr(r2, 5) ?? 0), e2.push(t2);
  return e2;
}
function eh(t2) {
  return Array.from(t2, (t3) => t3 > 127 ? t3 - 256 : t3);
}
function rh(t2, e2) {
  if (t2.length !== e2.length) throw Error(`Cannot compute cosine similarity between embeddings of different sizes (${t2.length} vs. ${e2.length}).`);
  var r2 = 0, n2 = 0, i2 = 0;
  for (let s2 = 0; s2 < t2.length; s2++) r2 += t2[s2] * e2[s2], n2 += t2[s2] * t2[s2], i2 += e2[s2] * e2[s2];
  if (n2 <= 0 || i2 <= 0) throw Error("Cannot compute cosine similarity on embedding with 0 norm.");
  return r2 / Math.sqrt(n2 * i2);
}
function nh(t2, e2, r2) {
  return r2 = `wasm${r2 ? "_module" : ""}_internal`, Promise.resolve({ wasmLoaderPath: `${e2}/${t2}_${r2}.js`, wasmBinaryPath: `${e2}/${t2}_${r2}.wasm` });
}
function sh(t2) {
  return sr(new oh(), 1, ee(t2));
}
function lh(t2, e2) {
  return sr(t2, 1, ee(e2));
}
function dh(t2, e2) {
  return sr(t2, 2, ee(e2));
}
function Ih(t2, e2) {
  var r2 = new gh();
  r2 = Lr(r2, 0, 1, t2.C), r2 = Lr(r2, 0, 2, e2), e2 = sr(e2 = new _h(), 6, lt(r2 = r2.h(), false)), (t2 = t2.m).error || t2.i.push(e2);
}
function Fh(t2, e2) {
  var r2 = { R: e2.R - t2.l.R, U: e2.U - t2.l.U, W: e2.W - t2.l.W, S: e2.S - t2.l.S, Y: e2.Y - t2.l.Y, V: e2.V, ba: e2.ba }, n2 = dh(lh(new fh(), t2.D), 1);
  r2 = Ch(t2, r2), Ih(t2, n2 = xr(n2, 4, ph, r2)), t2.l = e2;
}
function Ch(t2, e2) {
  var r2 = new ah();
  return t2 = $r(t2 = Pr(t2 = sr(r2, 1, ee(t2.F)), 7, e2.S), 5, e2.V), t2 = $r(t2, 6, e2.ba), e2.W > 0 && $r(t2, 4, e2.Y / e2.W), 0 !== e2.R && (r2 = Pr(r2 = sh(3), 2, e2.R), Fr(t2, 8, oh, r2)), 0 !== e2.U && (e2 = Pr(r2 = sh(4), 2, e2.U), Fr(t2, 8, oh, e2)), t2;
}
function Oh() {
  var t2 = navigator;
  return "undefined" != typeof OffscreenCanvas && (!(function(t3 = navigator) {
    return (t3 = t3.userAgent).includes("Safari") && !t3.includes("Chrome");
  })(t2) || !!((t2 = t2.userAgent.match(/Version\/([\d]+).*Safari/)) && t2.length >= 1 && Number(t2[1]) >= 17));
}
async function Mh(t2) {
  if ("function" != typeof importScripts) {
    let e2 = document.createElement("script");
    return e2.src = t2.toString(), e2.crossOrigin = "anonymous", new Promise((t3, r2) => {
      e2.addEventListener("load", () => {
        t3();
      }, false), e2.addEventListener("error", (t4) => {
        r2(t4);
      }, false), document.body.appendChild(e2);
    });
  }
  try {
    importScripts(t2.toString());
  } catch (e2) {
    if (!(e2 instanceof TypeError)) throw e2;
    {
      let e3 = self.import;
      e3 ? await e3(t2.toString()) : await import(t2.toString());
    }
  }
}
function Dh(t2) {
  return void 0 !== t2.videoWidth ? [t2.videoWidth, t2.videoHeight] : void 0 !== t2.naturalWidth ? [t2.naturalWidth, t2.naturalHeight] : void 0 !== t2.displayWidth ? [t2.displayWidth, t2.displayHeight] : [t2.width, t2.height];
}
function Ph(t2, e2, r2) {
  t2.o || console.error("No wasm multistream support detected: ensure dependency inclusion of :gl_graph_runner_internal_multi_input target"), r2(e2 = t2.j.stringToNewUTF8(e2)), t2.j._free(e2);
}
function $h(t2, e2, r2) {
  if (!t2.j.canvas) throw Error("No OpenGL canvas configured.");
  if (r2 ? t2.j._bindTextureToStream(r2) : t2.j._bindTextureToCanvas(), !(r2 = t2.j.canvas.getContext("webgl2") || t2.j.canvas.getContext("webgl"))) throw Error("Failed to obtain WebGL context from the provided canvas. `getContext()` should only be invoked with `webgl` or `webgl2`.");
  t2.j.gpuOriginForWebTexturesIsBottomLeft && r2.pixelStorei(r2.UNPACK_FLIP_Y_WEBGL, true), r2.texImage2D(r2.TEXTURE_2D, 0, r2.RGBA, r2.RGBA, r2.UNSIGNED_BYTE, e2), t2.j.gpuOriginForWebTexturesIsBottomLeft && r2.pixelStorei(r2.UNPACK_FLIP_Y_WEBGL, false);
  var [n2, i2] = Dh(e2);
  return !t2.l || n2 === t2.j.canvas.width && i2 === t2.j.canvas.height || (t2.j.canvas.width = n2, t2.j.canvas.height = i2), [n2, i2];
}
function Uh(t2, e2, r2) {
  t2.o || console.error("No wasm multistream support detected: ensure dependency inclusion of :gl_graph_runner_internal_multi_input target");
  var n2 = new Uint32Array(e2.length);
  for (let r3 = 0; r3 < e2.length; r3++) n2[r3] = t2.j.stringToNewUTF8(e2[r3]);
  e2 = t2.j._malloc(4 * n2.length), t2.j.HEAPU32.set(n2, e2 >> 2), r2(e2);
  for (let e3 of n2) t2.j._free(e3);
  t2.j._free(e2);
}
function Bh(t2, e2, r2) {
  t2.j.simpleListeners = t2.j.simpleListeners || {}, t2.j.simpleListeners[e2] = r2;
}
function jh(t2, e2, r2) {
  var n2 = [];
  t2.j.simpleListeners = t2.j.simpleListeners || {}, t2.j.simpleListeners[e2] = (t3, e3, i2) => {
    e3 ? (r2(n2, i2), n2 = []) : n2.push(t3);
  };
}
function Vh(t2) {
  return class extends t2 {
    get pa() {
      return this.j;
    }
    Sa() {
      if ("function" == typeof this.pa._mediapipeLoggerGetEncodedApiKey) {
        let t3 = this.pa._mediapipeLoggerGetEncodedApiKey();
        return this.pa._decodeBase64(t3);
      }
    }
  };
}
function Hh(t2) {
  return class extends t2 {
    Za() {
      this.j._registerModelResourcesGraphService();
    }
  };
}
async function Wh(t2, e2, r2, n2) {
  return t2 = await (async (t3, e3, r3, n3, i2) => {
    if (e3 && await Mh(e3), !self.ModuleFactory) throw Error("ModuleFactory not set.");
    if (r3 && (await Mh(r3), !self.ModuleFactory)) throw Error("ModuleFactory not set.");
    return self.Module && i2 && ((e3 = self.Module).locateFile = i2.locateFile, i2.mainScriptUrlOrBlob && (e3.mainScriptUrlOrBlob = i2.mainScriptUrlOrBlob)), i2 = await self.ModuleFactory(self.Module || i2), self.ModuleFactory = self.Module = void 0, new t3(i2, n3);
  })(t2, r2.wasmLoaderPath, r2.assetLoaderPath, e2, { locateFile: (t3) => t3.endsWith(".wasm") ? r2.wasmBinaryPath.toString() : r2.assetBinaryPath && t3.endsWith(".data") ? r2.assetBinaryPath.toString() : t3 }), (function(t3, e3) {
    e3 = e3.runningMode ?? "";
    var r3 = t3.h.Sa();
    t3.o = new Nh(t3.D(), e3, r3);
  })(t2, n2), await t2.A(n2), t2;
}
async function Yh(t2, e2, r2, n2) {
  return Wh(t2, e2, r2, n2);
}
function zh(t2, e2, r2) {
  try {
    t2.h.j.FS_unlink(`/${r2}`);
  } catch {
  }
  t2.h.j.FS_createDataFile("/", r2, e2, true, false, true), qh(t2, `/${r2}`);
}
function qh(t2, e2) {
  var r2 = Tr(t2.baseOptions, Wo, 1) || new Wo();
  "string" == typeof e2 ? (sr(r2, 2, ge(e2)), sr(r2, 1)) : e2 instanceof Uint8Array && (sr(r2, 1, lt(e2, false)), sr(r2, 2)), Lr(t2.baseOptions, 0, 1, r2);
}
function Jh(t2) {
  try {
    let e2 = t2.L.length;
    if (1 === e2) throw Error(t2.L[0].message);
    if (e2 > 1) throw Error("Encountered multiple errors: " + t2.L.map((t3) => t3.message).join(", "));
  } finally {
    t2.L = [];
  }
}
function Zh(t2, e2) {
  t2.J = Math.max(t2.J, e2);
}
function Qh(t2, e2) {
  t2.F = new ho(), jr(t2.F, 2, "PassThroughCalculator"), oo(t2.F, "free_memory"), ao(t2.F, "free_memory_unused_out"), po(e2, "free_memory"), fo(e2, t2.F);
}
function tu(t2, e2) {
  oo(t2.F, e2), ao(t2.F, e2 + "_unused_out");
}
function eu(t2) {
  t2.h.addBoolToStream(true, "free_memory", t2.J);
}
function nu(t2, e2) {
  if (!t2) throw Error(`Unable to obtain required WebGL resource: ${e2}`);
  return t2;
}
function su(t2, e2, r2) {
  var n2 = t2.h;
  if (r2 = nu(n2.createShader(r2), "Failed to create WebGL shader"), n2.shaderSource(r2, e2), n2.compileShader(r2), !n2.getShaderParameter(r2, n2.COMPILE_STATUS)) throw Error(`Could not compile WebGL shader: ${n2.getShaderInfoLog(r2)}`);
  return n2.attachShader(t2.i, r2), r2;
}
function ou(t2, e2) {
  var r2 = t2.h, n2 = nu(r2.createVertexArray(), "Failed to create vertex array");
  r2.bindVertexArray(n2);
  var i2 = nu(r2.createBuffer(), "Failed to create buffer");
  r2.bindBuffer(r2.ARRAY_BUFFER, i2), r2.enableVertexAttribArray(t2.G), r2.vertexAttribPointer(t2.G, 2, r2.FLOAT, false, 0, 0), r2.bufferData(r2.ARRAY_BUFFER, new Float32Array([-1, -1, -1, 1, 1, 1, 1, -1]), r2.STATIC_DRAW);
  var s2 = nu(r2.createBuffer(), "Failed to create buffer");
  return r2.bindBuffer(r2.ARRAY_BUFFER, s2), r2.enableVertexAttribArray(t2.L), r2.vertexAttribPointer(t2.L, 2, r2.FLOAT, false, 0, 0), r2.bufferData(r2.ARRAY_BUFFER, new Float32Array(e2 ? [0, 1, 0, 0, 1, 0, 1, 1] : [0, 0, 0, 1, 1, 1, 1, 0]), r2.STATIC_DRAW), r2.bindBuffer(r2.ARRAY_BUFFER, null), r2.bindVertexArray(null), new iu(r2, n2, i2, s2);
}
function au(t2, e2) {
  if (t2.h) {
    if (e2 !== t2.h) throw Error("Cannot change GL context once initialized");
  } else t2.h = e2;
}
function hu(t2, e2, r2, n2) {
  return au(t2, e2), t2.i || (t2.o(), t2.J()), r2 ? (t2.m || (t2.m = ou(t2, true)), r2 = t2.m) : (t2.F || (t2.F = ou(t2, false)), r2 = t2.F), e2.useProgram(t2.i), r2.bind(), t2.l(), t2 = n2(), r2.h.bindVertexArray(null), t2;
}
function uu(t2, e2, r2) {
  return au(t2, e2), t2 = nu(e2.createTexture(), "Failed to create texture"), e2.bindTexture(e2.TEXTURE_2D, t2), e2.texParameteri(e2.TEXTURE_2D, e2.TEXTURE_WRAP_S, e2.CLAMP_TO_EDGE), e2.texParameteri(e2.TEXTURE_2D, e2.TEXTURE_WRAP_T, e2.CLAMP_TO_EDGE), e2.texParameteri(e2.TEXTURE_2D, e2.TEXTURE_MIN_FILTER, r2 ?? e2.LINEAR), e2.texParameteri(e2.TEXTURE_2D, e2.TEXTURE_MAG_FILTER, r2 ?? e2.LINEAR), e2.bindTexture(e2.TEXTURE_2D, null), t2;
}
function cu(t2, e2, r2) {
  au(t2, e2), t2.D || (t2.D = nu(e2.createFramebuffer(), "Failed to create framebuffe.")), e2.bindFramebuffer(e2.FRAMEBUFFER, t2.D), e2.framebufferTexture2D(e2.FRAMEBUFFER, e2.COLOR_ATTACHMENT0, e2.TEXTURE_2D, r2, 0);
}
function lu(t2) {
  t2.h?.bindFramebuffer(t2.h.FRAMEBUFFER, null);
}
function mu(t2, e2) {
  switch (e2) {
    case 0:
      return t2.h.find((t3) => t3 instanceof Uint8Array);
    case 1:
      return t2.h.find((t3) => t3 instanceof Float32Array);
    case 2:
      return t2.h.find((t3) => "undefined" != typeof WebGLTexture && t3 instanceof WebGLTexture);
    default:
      throw Error(`Type is not supported: ${e2}`);
  }
}
function gu(t2) {
  var e2 = mu(t2, 1);
  if (!e2) {
    if (e2 = mu(t2, 0)) e2 = new Float32Array(e2).map((t3) => t3 / 255);
    else {
      e2 = new Float32Array(t2.width * t2.height);
      let n2 = _u(t2);
      var r2 = Eu(t2);
      if (cu(r2, n2, vu(t2)), "iPad Simulator;iPhone Simulator;iPod Simulator;iPad;iPhone;iPod".split(";").includes(navigator.platform) || navigator.userAgent.includes("Mac") && "document" in self && "ontouchend" in self.document) {
        r2 = new Float32Array(t2.width * t2.height * 4), n2.readPixels(0, 0, t2.width, t2.height, n2.RGBA, n2.FLOAT, r2);
        for (let t3 = 0, n3 = 0; t3 < e2.length; ++t3, n3 += 4) e2[t3] = r2[n3];
      } else n2.readPixels(0, 0, t2.width, t2.height, n2.RED, n2.FLOAT, e2);
    }
    t2.h.push(e2);
  }
  return e2;
}
function vu(t2) {
  var e2 = mu(t2, 2);
  if (!e2) {
    let r2 = _u(t2);
    e2 = Au(t2);
    let n2 = gu(t2), i2 = yu(t2);
    r2.texImage2D(r2.TEXTURE_2D, 0, i2, t2.width, t2.height, 0, r2.RED, r2.FLOAT, n2), wu(t2);
  }
  return e2;
}
function _u(t2) {
  if (!t2.canvas) throw Error("Conversion to different image formats require that a canvas is passed when initializing the image.");
  return t2.i || (t2.i = nu(t2.canvas.getContext("webgl2"), "You cannot use a canvas that is already bound to a different type of rendering context.")), t2.i;
}
function yu(t2) {
  if (t2 = _u(t2), !Tu) if (t2.getExtension("EXT_color_buffer_float") && t2.getExtension("OES_texture_float_linear") && t2.getExtension("EXT_float_blend")) Tu = t2.R32F;
  else {
    if (!t2.getExtension("EXT_color_buffer_half_float")) throw Error("GPU does not fully support 4-channel float32 or float16 formats");
    Tu = t2.R16F;
  }
  return Tu;
}
function Eu(t2) {
  return t2.l || (t2.l = new du()), t2.l;
}
function Au(t2) {
  var e2 = _u(t2);
  e2.viewport(0, 0, t2.width, t2.height), e2.activeTexture(e2.TEXTURE0);
  var r2 = mu(t2, 2);
  return r2 || (r2 = uu(Eu(t2), e2, t2.o ? e2.LINEAR : e2.NEAREST), t2.h.push(r2), t2.u = true), e2.bindTexture(e2.TEXTURE_2D, r2), r2;
}
function wu(t2) {
  t2.i.bindTexture(t2.i.TEXTURE_2D, null);
}
function Lu(t2) {
  return { ...Su, fillColor: (t2 = t2 || {}).color, ...t2 };
}
function xu(t2, e2) {
  return t2 instanceof Function ? t2(e2) : t2;
}
function Ru(t2, e2, r2) {
  return Math.max(Math.min(e2, r2), Math.min(Math.max(e2, r2), t2));
}
function Iu(t2) {
  if (!t2.l) throw Error("CPU rendering requested but CanvasRenderingContext2D not provided.");
  return t2.l;
}
function Fu(t2) {
  if (!t2.u) throw Error("GPU rendering requested but WebGL2RenderingContext not provided.");
  return t2.u;
}
function Cu(t2, e2, r2) {
  if (e2.X()) r2(e2.T());
  else {
    let n2 = e2.ua() ? e2.sa() : e2.ta();
    t2.o = t2.o ?? new du();
    let i2 = Fu(t2);
    r2((t2 = new bu([n2], e2.o, false, i2.canvas, t2.o, e2.width, e2.height)).T()), t2.close();
  }
}
function Nu(t2, e2, r2, n2) {
  var i2 = (function(t3) {
    return t3.h || (t3.h = new fu()), t3.h;
  })(t2), s2 = Fu(t2), o2 = Array.isArray(r2) ? new ImageData(new Uint8ClampedArray(r2), 1, 1) : r2;
  hu(i2, s2, true, () => {
    !(function(t4, e3, r3, n3) {
      var i3 = t4.h;
      if (i3.activeTexture(i3.TEXTURE0), i3.bindTexture(i3.TEXTURE_2D, e3), i3.activeTexture(i3.TEXTURE1), i3.bindTexture(i3.TEXTURE_2D, t4.v), i3.texImage2D(i3.TEXTURE_2D, 0, i3.RGBA, i3.RGBA, i3.UNSIGNED_BYTE, r3), t4.K && (function(t5, e4) {
        if (t5 !== e4) return false;
        t5 = t5.entries(), e4 = e4.entries();
        for (let [r4, n4] of t5) {
          t5 = r4;
          let i4 = n4, s3 = e4.next();
          if (s3.done) return false;
          let [o3, a2] = s3.value;
          if (t5 !== o3 || i4[0] !== a2[0] || i4[1] !== a2[1] || i4[2] !== a2[2] || i4[3] !== a2[3]) return false;
        }
        return !!e4.next().done;
      })(t4.K, n3)) i3.activeTexture(i3.TEXTURE2), i3.bindTexture(i3.TEXTURE_2D, t4.u);
      else {
        t4.K = n3;
        let e4 = Array(1024).fill(0);
        n3.forEach((t5, r4) => {
          if (4 !== t5.length) throw Error(`Color at index ${r4} is not a four-channel value.`);
          e4[4 * r4] = t5[0], e4[4 * r4 + 1] = t5[1], e4[4 * r4 + 2] = t5[2], e4[4 * r4 + 3] = t5[3];
        }), i3.activeTexture(i3.TEXTURE2), i3.bindTexture(i3.TEXTURE_2D, t4.u), i3.texImage2D(i3.TEXTURE_2D, 0, i3.RGBA, 256, 1, 0, i3.RGBA, i3.UNSIGNED_BYTE, new Uint8Array(e4));
      }
    })(i2, e2, o2, n2), s2.clearColor(0, 0, 0, 0), s2.clear(s2.COLOR_BUFFER_BIT), s2.drawArrays(s2.TRIANGLE_FAN, 0, 4);
    var t3 = i2.h;
    t3.activeTexture(t3.TEXTURE0), t3.bindTexture(t3.TEXTURE_2D, null), t3.activeTexture(t3.TEXTURE1), t3.bindTexture(t3.TEXTURE_2D, null), t3.activeTexture(t3.TEXTURE2), t3.bindTexture(t3.TEXTURE_2D, null);
  });
}
function Ou(t2, e2, r2, n2) {
  var i2 = Fu(t2), s2 = (function(t3) {
    return t3.i || (t3.i = new pu()), t3.i;
  })(t2), o2 = Array.isArray(r2) ? new ImageData(new Uint8ClampedArray(r2), 1, 1) : r2, a2 = Array.isArray(n2) ? new ImageData(new Uint8ClampedArray(n2), 1, 1) : n2;
  hu(s2, i2, true, () => {
    var t3 = s2.h;
    t3.activeTexture(t3.TEXTURE0), t3.bindTexture(t3.TEXTURE_2D, e2), t3.activeTexture(t3.TEXTURE1), t3.bindTexture(t3.TEXTURE_2D, s2.u), t3.texImage2D(t3.TEXTURE_2D, 0, t3.RGBA, t3.RGBA, t3.UNSIGNED_BYTE, o2), t3.activeTexture(t3.TEXTURE2), t3.bindTexture(t3.TEXTURE_2D, s2.v), t3.texImage2D(t3.TEXTURE_2D, 0, t3.RGBA, t3.RGBA, t3.UNSIGNED_BYTE, a2), i2.clearColor(0, 0, 0, 0), i2.clear(i2.COLOR_BUFFER_BIT), i2.drawArrays(i2.TRIANGLE_FAN, 0, 4), i2.bindTexture(i2.TEXTURE_2D, null), (t3 = s2.h).activeTexture(t3.TEXTURE0), t3.bindTexture(t3.TEXTURE_2D, null), t3.activeTexture(t3.TEXTURE1), t3.bindTexture(t3.TEXTURE_2D, null), t3.activeTexture(t3.TEXTURE2), t3.bindTexture(t3.TEXTURE_2D, null);
  });
}
function Du(t2, e2) {
  switch (e2) {
    case 0:
      return t2.h.find((t3) => t3 instanceof ImageData);
    case 1:
      return t2.h.find((t3) => "undefined" != typeof ImageBitmap && t3 instanceof ImageBitmap);
    case 2:
      return t2.h.find((t3) => "undefined" != typeof WebGLTexture && t3 instanceof WebGLTexture);
    default:
      throw Error(`Type is not supported: ${e2}`);
  }
}
function Pu(t2) {
  var e2 = Du(t2, 0);
  if (!e2) {
    e2 = Uu(t2);
    let r2 = Bu(t2), n2 = new Uint8Array(t2.width * t2.height * 4);
    cu(r2, e2, $u(t2)), e2.readPixels(0, 0, t2.width, t2.height, e2.RGBA, e2.UNSIGNED_BYTE, n2), lu(r2), e2 = new ImageData(new Uint8ClampedArray(n2.buffer), t2.width, t2.height), t2.h.push(e2);
  }
  return e2;
}
function $u(t2) {
  var e2 = Du(t2, 2);
  if (!e2) {
    let r2 = Uu(t2);
    e2 = ju(t2);
    let n2 = Du(t2, 1) || Pu(t2);
    r2.texImage2D(r2.TEXTURE_2D, 0, r2.RGBA, r2.RGBA, r2.UNSIGNED_BYTE, n2), Gu(t2);
  }
  return e2;
}
function Uu(t2) {
  if (!t2.canvas) throw Error("Conversion to different image formats require that a canvas is passed when initializing the image.");
  return t2.i || (t2.i = nu(t2.canvas.getContext("webgl2"), "You cannot use a canvas that is already bound to a different type of rendering context.")), t2.i;
}
function Bu(t2) {
  return t2.l || (t2.l = new du()), t2.l;
}
function ju(t2) {
  var e2 = Uu(t2);
  e2.viewport(0, 0, t2.width, t2.height), e2.activeTexture(e2.TEXTURE0);
  var r2 = Du(t2, 2);
  return r2 || (r2 = uu(Bu(t2), e2), t2.h.push(r2), t2.o = true), e2.bindTexture(e2.TEXTURE_2D, r2), r2;
}
function Gu(t2) {
  t2.i.bindTexture(t2.i.TEXTURE_2D, null);
}
function Vu(t2) {
  var e2 = Uu(t2);
  return hu(Bu(t2), e2, true, () => (function(t3, e3) {
    var r2 = t3.canvas;
    if (r2.width === t3.width && r2.height === t3.height) return e3();
    var n2 = r2.width, i2 = r2.height;
    return r2.width = t3.width, r2.height = t3.height, t3 = e3(), r2.width = n2, r2.height = i2, t3;
  })(t2, () => {
    if (e2.bindFramebuffer(e2.FRAMEBUFFER, null), e2.clearColor(0, 0, 0, 0), e2.clear(e2.COLOR_BUFFER_BIT), e2.drawArrays(e2.TRIANGLE_FAN, 0, 4), !(t2.canvas instanceof OffscreenCanvas)) throw Error("Conversion to ImageBitmap requires that the MediaPipe Tasks is initialized with an OffscreenCanvas");
    return t2.canvas.transferToImageBitmap();
  }));
}
function Xu(...t2) {
  return t2.map(([t3, e2]) => ({ start: t3, end: e2 }));
}
async function qu(t2, e2, r2) {
  return Yh(t2, r2.canvas ?? (Oh() ? void 0 : document.createElement("canvas")), e2, r2);
}
function Ju(t2, e2, r2, n2) {
  if (t2.o && void 0 !== n2) {
    var i2 = Tr(t2.baseOptions, Ko, 3);
    i2?.h() || i2?.l()?.h() ? (++(i2 = t2.o).h.U, i2.i.set(n2, performance.now())) : (++(i2 = t2.o).h.R, i2.i.set(n2, performance.now()));
  }
  if (t2.qa) {
    if (i2 = new No(), r2?.regionOfInterest) {
      if (!t2.Ca) throw Error("This task doesn't support region-of-interest.");
      let e3 = r2.regionOfInterest;
      if (e3.left >= e3.right || e3.top >= e3.bottom) throw Error("Expected RectF with left < right and top < bottom.");
      if (e3.left < 0 || e3.top < 0 || e3.right > 1 || e3.bottom > 1) throw Error("Expected RectF values to be in [0,1].");
      Ur(i2, 1, (e3.left + e3.right) / 2), Ur(i2, 2, (e3.top + e3.bottom) / 2), Ur(i2, 4, e3.right - e3.left), Ur(i2, 3, e3.bottom - e3.top);
    } else Ur(i2, 1, 0.5), Ur(i2, 2, 0.5), Ur(i2, 4, 1), Ur(i2, 3, 1);
    if (r2?.rotationDegrees) {
      if (r2?.rotationDegrees % 90 != 0) throw Error("Expected rotation to be a multiple of 90\xB0.");
      if (Ur(i2, 5, -Math.PI * r2.rotationDegrees / 180), r2?.rotationDegrees % 180 != 0) {
        let [t3, n3] = Dh(e2);
        r2 = Nr(i2, 4) * t3 / n3, Ur(i2, 4, Nr(i2, 3) * n3 / t3), Ur(i2, 3, r2);
      }
    }
    t2.h.addProtoToStream(i2.h(), "mediapipe.NormalizedRect", t2.qa, n2);
  }
  t2.h.Da(e2, t2.Ba, n2 ?? performance.now()), t2.finishProcessing(n2);
}
function Zu(t2, e2, r2) {
  if (t2.K) throw Error("Task is not initialized with image mode. 'runningMode' must be set to 'IMAGE'.");
  Ju(t2, e2, r2, t2.J + 1);
}
function Qu(t2, e2, r2, n2) {
  if (!t2.K) throw Error("Task is not initialized with video mode. 'runningMode' must be set to 'VIDEO'.");
  Ju(t2, e2, r2, n2);
}
function tc(t2, e2, r2, n2) {
  var i2 = e2.data, s2 = e2.width, o2 = s2 * (e2 = e2.height);
  if ((i2 instanceof Uint8Array || i2 instanceof Float32Array) && i2.length !== o2) throw Error("Unsupported channel count: " + i2.length / o2);
  return t2 = new bu([i2], r2, false, t2.h.j.canvas, t2.ea, s2, e2), n2 ? t2.clone() : t2;
}
function fc(t2) {
  t2.m = { faceLandmarks: [], faceBlendshapes: [], facialTransformationMatrixes: [] };
}
function gc(t2) {
  t2.gestures = [], t2.landmarks = [], t2.worldLandmarks = [], t2.handedness = [];
}
function vc(t2) {
  return 0 === t2.gestures.length ? { gestures: [], landmarks: [], worldLandmarks: [], handedness: [], handednesses: [] } : { gestures: t2.gestures, landmarks: t2.landmarks, worldLandmarks: t2.worldLandmarks, handedness: t2.handedness, handednesses: t2.handedness };
}
function _c(t2, e2 = true) {
  var r2 = [];
  for (let i2 of t2) {
    var n2 = wo(i2);
    t2 = [];
    for (let r3 of n2.h()) n2 = e2 && null != Cr(r3, 1) ? Cr(r3, 1) ?? 0 : -1, t2.push({ score: Nr(r3, 2) ?? 0, index: n2, categoryName: ve(nr(r3, 3)) ?? "" ?? "", displayName: ve(nr(r3, 4)) ?? "" ?? "" });
    r2.push(t2);
  }
  return r2;
}
function Ec(t2) {
  return { landmarks: t2.landmarks, worldLandmarks: t2.worldLandmarks, handednesses: t2.handedness, handedness: t2.handedness };
}
function Tc(t2) {
  t2.i = { faceLandmarks: [], faceBlendshapes: [], poseLandmarks: [], poseWorldLandmarks: [], poseSegmentationMasks: [], leftHandLandmarks: [], leftHandWorldLandmarks: [], rightHandLandmarks: [], rightHandWorldLandmarks: [] };
}
function bc(t2) {
  try {
    if (!t2.G) return t2.i;
    t2.G(t2.i);
  } finally {
    eu(t2);
  }
}
function kc(t2, e2) {
  t2 = Io(t2), e2.push(Qa(t2));
}
function Ic(t2) {
  var e2 = (function(t3) {
    return kr(t3, ho, 1);
  })(t2.ka()).filter((t3) => (ve(nr(t3, 1)) ?? "").includes("mediapipe.tasks.TensorsToSegmentationCalculator"));
  if (t2.v = [], e2.length > 1) throw Error("The graph has more than one mediapipe.tasks.TensorsToSegmentationCalculator.");
  1 === e2.length && (Tr(e2[0], no, 7)?.u()?.h() ?? /* @__PURE__ */ new Map()).forEach((e3, r2) => {
    t2.v[Number(r2)] = ve(nr(e3, 1)) ?? "";
  });
}
function Fc(t2) {
  t2.categoryMask = void 0, t2.confidenceMasks = void 0, t2.qualityScores = void 0;
}
function Cc(t2) {
  try {
    let e2 = new Rc(t2.confidenceMasks, t2.categoryMask, t2.qualityScores);
    if (!t2.m) return e2;
    t2.m(e2);
  } finally {
    eu(t2);
  }
}
function Mc() {
  return Oh() ? void 0 : document.createElement("canvas");
}
function jc(t2) {
  t2.landmarks = [], t2.worldLandmarks = [], t2.segmentationMasks = void 0;
}
function Gc(t2) {
  try {
    let e2 = new Bc(t2.landmarks, t2.worldLandmarks, t2.segmentationMasks);
    if (!t2.v) return e2;
    t2.v(e2);
  } finally {
    eu(t2);
  }
}
var t, i, s, o, a, h, u, c, d, f, p, m, _, y, w, T, b, k, R, C, N, D, B, G, V, H, K, X, W, Y, z, q, J, Z, Q, tt, et, at, ct, dt, pt, vt, _t, yt, Et, wt, Tt, bt, kt, St, xt, Rt, It, Ft, Ht, Kt, Xt, Wt, Yt, zt, Qt, Ae, we, Te, be, Le, xe, Oe, Me, De, Pe, er, rr, Hr, nn, sn, on, gn, vn, En, An, bn, kn, Sn, Ln, xn, Rn, In, Fn, Cn, Un, Wn, qn, Jn, Zn, Qn, ti, ei, ri, ni, ii, si, oi, ai, ui, ci, fi, pi, mi, gi, vi, _i, yi, Ei, Ai, Oi, Di, Bi, ts, es, rs, ns, is, ss, os, as, hs, us, cs, ls, ds, fs, ps, ms, gs, vs, _s, ys, Es, As, ws, Ts, bs, ks, Ss, Ls, xs, Rs, Os, Ms, Ds, Ps, $s, Us, Bs, js, Hs, Ks, Xs, Ws, Ys, zs, qs, Js, Zs, Qs, to, eo, ro, no, io, so, ho, uo, co, lo, go, vo, _o, yo, Eo, Ao, wo, To, bo, ko, So, Lo, xo, Ro, Io, Fo, Co, No, Oo, Mo, Do, Po, $o, Uo, Bo, jo, Go, Vo, Ho, Ko, Xo, Wo, Yo, zo, qo, Jo, Zo, Qo, ta, ea, ra, na, ia, sa, oa, aa, ha, ua, ca, la, da, fa, pa, ma, ga, va, _a, ya, Ea, Aa, wa, Ta, ba, ka, Sa, La, xa, Ra, Ia, Fa, Ca, Na, Oa, Ma, Da, Pa, $a, Ua, Ba, ja, Ga, Va, Ha, Ka, Xa, Wa, ih, oh, ah, hh, uh, ch, fh, ph, mh, gh, vh, _h, yh, Eh, Ah, wh, Th, bh, kh, Sh, Lh, xh, Rh, Nh, Gh, Kh, Xh, ru, iu, du, fu, pu, Tu, bu, ku, Su, Mu, Hu, Ku, Wu, Yu, zu, ec, rc, nc, ic, sc, oc, ac, hc, uc, cc, lc, dc, pc, mc, yc, Ac, wc, Sc, Lc, xc, Rc, Nc, Oc, Dc, Pc, $c, Uc, Bc, Vc;
var init_vision_bundle = __esm({
  "node_modules/@mediapipe/tasks-vision/vision_bundle.mjs"() {
    t = "undefined" != typeof self ? self : {};
    h = void 0;
    u = "undefined" != typeof TextDecoder;
    c = "undefined" != typeof TextEncoder;
    t: {
      for (d = ["CLOSURE_FLAGS"], f = t, p = 0; p < d.length; p++) if (null == (f = f[d[p]])) {
        a = null;
        break t;
      }
      a = f;
    }
    m = a && a[748402147];
    o = null == m || m, v[" "] = function() {
    };
    _ = {};
    y = null;
    w = "undefined" != typeof Uint8Array;
    T = !(-1 != g().indexOf("Trident") || -1 != g().indexOf("MSIE")) && "function" == typeof btoa;
    b = /[-_.]/g;
    k = { "-": "+", _: "/", ".": "=" };
    R = {};
    N = class {
      i() {
        return new Uint8Array(F(this) || 0);
      }
      constructor(t2, e2) {
        if (O(e2), this.h = t2, null != t2 && 0 === t2.length) throw Error("ByteString should be constructed with non-empty values");
      }
    };
    D = void 0;
    B = "function" == typeof Symbol && "symbol" == typeof /* @__PURE__ */ Symbol();
    V = j("jas", void 0, true);
    H = j(void 0, "0di");
    K = j(void 0, "1oa");
    X = j(void 0, /* @__PURE__ */ Symbol());
    W = j(void 0, "0ub");
    Y = j(void 0, "0ubs");
    z = j(void 0, "0ubsb");
    q = j(void 0, "0actk");
    J = j("m_m", "jb", true);
    Z = { Va: { value: 0, configurable: true, writable: true, enumerable: false } };
    Q = Object.defineProperties;
    tt = B ? V : "Va";
    et = [];
    nt(et, 7), G = Object.freeze(et);
    at = {};
    ct = {};
    dt = class {
      constructor(t2, e2, r2) {
        this.h = t2, this.i = e2, this.l = r2;
      }
      next() {
        var t2 = this.h.next();
        return t2.done || (t2.value = this.i.call(this.l, t2.value)), t2;
      }
      [Symbol.iterator]() {
        return this;
      }
    };
    Object.freeze({});
    pt = {};
    vt = gt((t2) => "number" == typeof t2);
    _t = gt((t2) => "string" == typeof t2);
    yt = gt((t2) => "boolean" == typeof t2);
    Et = "function" == typeof t.BigInt && "bigint" == typeof t.BigInt(0);
    wt = gt((t2) => Et ? t2 >= bt && t2 <= St : "-" === t2[0] ? Lt(t2, Tt) : Lt(t2, kt));
    Tt = Number.MIN_SAFE_INTEGER.toString();
    bt = Et ? BigInt(Number.MIN_SAFE_INTEGER) : void 0;
    kt = Number.MAX_SAFE_INTEGER.toString();
    St = Et ? BigInt(Number.MAX_SAFE_INTEGER) : void 0;
    Rt = "function" == typeof Uint8Array.prototype.slice;
    It = 0;
    Ft = 0;
    Ht = "function" == typeof BigInt ? BigInt.asIntN : void 0;
    Kt = "function" == typeof BigInt ? BigInt.asUintN : void 0;
    Xt = Number.isSafeInteger;
    Wt = Number.isFinite;
    Yt = Math.trunc;
    zt = At(0);
    Qt = /^-?([1-9][0-9]*|0)(\.[0-9]+)?$/;
    Ae = {};
    we = (function() {
      try {
        return v(new class extends Map {
          constructor() {
            super();
          }
        }()), false;
      } catch {
        return true;
      }
    })();
    Te = class {
      constructor() {
        this.h = /* @__PURE__ */ new Map();
      }
      get(t2) {
        return this.h.get(t2);
      }
      set(t2, e2) {
        return this.h.set(t2, e2), this.size = this.h.size, this;
      }
      delete(t2) {
        return t2 = this.h.delete(t2), this.size = this.h.size, t2;
      }
      clear() {
        this.h.clear(), this.size = this.h.size;
      }
      has(t2) {
        return this.h.has(t2);
      }
      entries() {
        return this.h.entries();
      }
      keys() {
        return this.h.keys();
      }
      values() {
        return this.h.values();
      }
      forEach(t2, e2) {
        return this.h.forEach(t2, e2);
      }
      [Symbol.iterator]() {
        return this.entries();
      }
    };
    be = we ? (Object.setPrototypeOf(Te.prototype, Map.prototype), Object.defineProperties(Te.prototype, { size: { value: 0, configurable: true, enumerable: true, writable: true } }), Te) : class extends Map {
      constructor() {
        super();
      }
    };
    xe = class extends be {
      constructor(t2, e2, r2 = ke, n2 = ke) {
        super(), this.N = 0 | t2[tt], this.O = e2, this.ca = r2, this.na = this.O ? Re : n2;
        for (let i2 = 0; i2 < t2.length; i2++) {
          let s2 = t2[i2], o2 = r2(s2[0], false, true), a2 = s2[1];
          e2 ? void 0 === a2 && (a2 = null) : a2 = n2(s2[1], false, true, void 0, void 0, this.N), super.set(o2, a2);
        }
      }
      fa(t2) {
        return st(Array.from(super.entries(), t2));
      }
      clear() {
        Se(this), super.clear();
      }
      delete(t2) {
        return Se(this), super.delete(this.ca(t2, true, false));
      }
      entries() {
        if (this.O) {
          var t2 = super.keys();
          t2 = new dt(t2, Ie, this);
        } else t2 = super.entries();
        return t2;
      }
      values() {
        if (this.O) {
          var t2 = super.keys();
          t2 = new dt(t2, xe.prototype.get, this);
        } else t2 = super.values();
        return t2;
      }
      forEach(t2, e2) {
        this.O ? super.forEach((r2, n2, i2) => {
          t2.call(e2, i2.get(n2), n2, i2);
        }) : super.forEach(t2, e2);
      }
      set(t2, e2) {
        return Se(this), null == (t2 = this.ca(t2, true, false)) ? this : null == e2 ? (super.delete(t2), this) : super.set(t2, this.na(e2, true, true, this.O, false, this.N));
      }
      fb(t2) {
        var e2 = this.ca(t2[0], false, true);
        t2 = t2[1], t2 = this.O ? void 0 === t2 ? null : t2 : this.na(t2, false, true, void 0, false, this.N), super.set(e2, t2);
      }
      has(t2) {
        return super.has(this.ca(t2, false, false));
      }
      get(t2) {
        t2 = this.ca(t2, false, false);
        var e2 = super.get(t2);
        if (void 0 !== e2) {
          var r2 = this.O;
          return r2 ? ((r2 = this.na(e2, false, true, r2, this.Fa, this.N)) !== e2 && super.set(t2, r2), r2) : e2;
        }
      }
      [Symbol.iterator]() {
        return this.entries();
      }
    };
    xe.prototype.toJSON = void 0;
    Pe = class {
    };
    er = At(0);
    rr = {};
    Hr = class {
      constructor(t2, e2, r2) {
        if (this.buffer = t2, r2 && !e2) throw Error();
        this.h = e2;
      }
    };
    nn = class {
      constructor(t2, e2, r2, n2) {
        this.i = null, this.u = false, this.h = this.l = this.o = 0, this.init(t2, e2, r2, n2);
      }
      init(t2, e2, r2, { ga: n2 = false, ma: i2 = false } = {}) {
        this.ga = n2, this.ma = i2, t2 && (t2 = Kr(t2, this.ma), this.i = t2.buffer, this.u = t2.h, this.o = e2 || 0, this.l = void 0 !== r2 ? this.o + r2 : this.i.length, this.h = this.o);
      }
      clear() {
        this.i = null, this.u = false, this.h = this.l = this.o = 0, this.ga = false;
      }
    };
    sn = [];
    on = 0;
    gn = class {
      constructor(t2, e2, r2, n2) {
        if (sn.length) {
          let i2 = sn.pop();
          i2.init(t2, e2, r2, n2), t2 = i2;
        } else t2 = new nn(t2, e2, r2, n2);
        this.h = t2, this.o = this.h.h, this.i = this.l = -1, this.A(n2);
      }
      A({ ra: t2 = false } = {}) {
        this.ra = t2;
      }
    };
    vn = [];
    An = class {
      constructor(t2, e2) {
        this.i = t2 >>> 0, this.h = e2 >>> 0;
      }
    };
    Cn = class {
      constructor(t2, e2) {
        this.i = t2 >>> 0, this.h = e2 >>> 0;
      }
    };
    Un = class {
      constructor() {
        this.h = [];
      }
      length() {
        return this.h.length;
      }
      end() {
        var t2 = this.h;
        return this.h = [], t2;
      }
    };
    Wn = class {
      constructor() {
        this.l = [], this.i = 0, this.h = new Un();
      }
    };
    qn = zn();
    Jn = zn();
    Zn = zn();
    Qn = zn();
    ti = zn();
    ei = zn();
    ri = zn();
    ni = zn();
    ii = zn();
    si = zn();
    oi = zn();
    ai = zn();
    ui = class {
      constructor(t2, e2) {
        this.B = He(t2, e2, void 0, 2048);
      }
      toJSON() {
        return Ge(this);
      }
      u() {
        var t2 = Ca, e2 = this.B, r2 = t2.h, n2 = X;
        return B && n2 && null != e2[n2]?.[r2] && $(W, 3), t2.i ? t2.o(this, t2.i, t2.h, t2.l) : t2.o(this, t2.h, t2.defaultValue, t2.l);
      }
      clone() {
        var t2 = this.B, e2 = 0 | t2[tt];
        return tr(this, t2, e2) ? We(this, t2, true) : new this.constructor(ze(t2, e2, false));
      }
    };
    ui.prototype[J] = at, ui.prototype.toString = function() {
      return this.B.toString();
    };
    ci = class {
      constructor(t2, e2, r2) {
        this.h = t2, this.i = e2, t2 = qn, this.l = !!t2 && r2 === t2 || false;
      }
    };
    mi = li(function(t2, e2, r2, n2, i2) {
      return 2 === t2.i && (dn(t2, Ar(e2, n2, r2), i2), true);
    }, di);
    gi = li(function(t2, e2, r2, n2, i2) {
      return 2 === t2.i && (dn(t2, Ar(e2, n2, r2), i2), true);
    }, di);
    vi = /* @__PURE__ */ Symbol();
    _i = /* @__PURE__ */ Symbol();
    yi = /* @__PURE__ */ Symbol();
    Ei = /* @__PURE__ */ Symbol();
    Ai = /* @__PURE__ */ Symbol();
    Oi = At(0);
    Di = (t2, e2) => {
      var r2 = new Wn();
      Ni(t2.B, r2, wi(vi, Ii, Fi, e2)), Bn(r2, r2.h.end()), t2 = new Uint8Array(r2.i);
      var n2 = (e2 = r2.l).length, i2 = 0;
      for (let r3 = 0; r3 < n2; r3++) {
        let n3 = e2[r3];
        t2.set(n3, i2), i2 += n3.length;
      }
      return r2.l = [t2], t2;
    };
    Bi = li(function(t2, e2, r2, n2, i2) {
      if (2 !== t2.i) return false;
      if (t2 = Vt(t2 = dn(t2, Ve([void 0, void 0], n2), i2)), i2 = mt(n2 = 0 | e2[tt]), 2 & n2) throw Error();
      var s2 = ir(e2, r2, i2);
      if (s2 instanceof xe) 2 & s2.N ? ((s2 = s2.fa()).push(t2), or(e2, n2, r2, s2, i2)) : s2.fb(t2);
      else if (Array.isArray(s2)) {
        var o2 = 0 | s2[tt];
        8192 & o2 || nt(s2, o2 |= 8192), 2 & o2 && or(e2, n2, r2, s2 = pr(s2), i2), s2.push(t2);
      } else or(e2, n2, r2, st([t2]), i2);
      return true;
    }, function(t2, e2, r2, n2, i2) {
      if (e2 instanceof xe) e2.forEach((e3, s2) => {
        Xn(t2, r2, Ve([s2, e3], n2), i2);
      });
      else if (Array.isArray(e2)) {
        for (let s2 = 0; s2 < e2.length; s2++) {
          let o2 = e2[s2];
          Array.isArray(o2) && Xn(t2, r2, Ve(o2, n2), i2);
        }
        st(e2);
      }
    });
    ts = Pi(function(t2, e2, r2) {
      if (1 !== t2.i) return false;
      var n2 = t2.h;
      t2 = Jr(n2);
      var i2 = Jr(n2);
      n2 = 2 * (i2 >> 31) + 1;
      var s2 = i2 >>> 20 & 2047;
      return t2 = 4294967296 * (1048575 & i2) + t2, Ui(e2, r2, 2047 == s2 ? t2 ? NaN : n2 * (1 / 0) : 0 == s2 ? 5e-324 * n2 * t2 : n2 * Math.pow(2, s2 - 1075) * (t2 + 4503599627370496)), true;
    }, function(t2, e2, r2) {
      null != (e2 = Jt(e2)) && (jn(t2, r2, 1), t2 = t2.h, (r2 = xt ||= new DataView(new ArrayBuffer(8))).setFloat64(0, +e2, true), It = r2.getUint32(0, true), Ft = r2.getUint32(4, true), $n(t2, It), $n(t2, Ft));
    }, si);
    es = Pi(function(t2, e2, r2) {
      return 5 === t2.i && (Ui(e2, r2, Zr(t2.h)), true);
    }, ji, ii);
    rs = $i(qi, function(t2, e2, r2) {
      if (null != (e2 = Mi(Jt, e2, true))) for (let o2 = 0; o2 < e2.length; o2++) {
        var n2 = t2, i2 = r2, s2 = e2[o2];
        null != s2 && (jn(n2, i2, 5), n2 = n2.h, Ot(s2), $n(n2, It));
      }
    }, ii);
    ns = $i(qi, function(t2, e2, r2) {
      if (null != (e2 = Mi(Jt, e2, true)) && e2.length) {
        jn(t2, r2, 2), Mn(t2.h, 4 * e2.length);
        for (let n2 = 0; n2 < e2.length; n2++) r2 = t2.h, Ot(e2[n2]), $n(r2, It);
      }
    }, ii);
    is = Pi(function(t2, e2, r2) {
      return 5 === t2.i && (Ui(e2, r2, 0 === (t2 = Zr(t2.h)) ? void 0 : t2), true);
    }, ji, ii);
    ss = Pi(function(t2, e2, r2) {
      return Ji(t2, e2, r2);
    }, Gi, ei);
    os = Pi(function(t2, e2, r2) {
      return Ji(t2, e2, r2);
    }, Gi, ei);
    as = $i(function(t2, e2, r2) {
      return 0 !== t2.i && 2 !== t2.i ? t2 = false : (e2 = gr(e2, 0 | e2[tt], r2), 2 == t2.i ? mn(t2, qr, e2) : e2.push(qr(t2.h)), t2 = true), t2;
    }, function(t2, e2, r2) {
      if (null != (e2 = Mi(de, e2, false))) for (let n2 = 0; n2 < e2.length; n2++) Hn(t2, r2, e2[n2]);
    }, ei);
    hs = Pi(function(t2, e2, r2) {
      return 0 !== t2.i ? e2 = false : (Ui(e2, r2, (t2 = qr(t2.h)) === Oi ? void 0 : t2), e2 = true), e2;
    }, Gi, ei);
    us = Pi(function(t2, e2, r2) {
      return 0 !== t2.i ? t2 = false : (Ui(e2, r2, Xr(t2.h, Dt)), t2 = true), t2;
    }, function(t2, e2, r2) {
      if (null != (e2 = fe(e2))) {
        if ("string" == typeof e2) yn(e2);
        if (null != e2) switch (jn(t2, r2, 0), typeof e2) {
          case "number":
            t2 = t2.h, Nt(e2), On(t2, It, Ft);
            break;
          case "bigint":
            r2 = Nn(e2, An, _n), On(t2.h, r2.i, r2.h);
            break;
          default:
            r2 = yn(e2), On(t2.h, r2.i, r2.h);
        }
      }
    }, ri);
    cs = Pi(function(t2, e2, r2) {
      return 0 === t2.i && (Ui(e2, r2, Yr(t2.h)), true);
    }, Vi, Qn);
    ls = $i(Zi, function(t2, e2, r2) {
      if (null != (e2 = Mi(re, e2, true))) for (let o2 = 0; o2 < e2.length; o2++) {
        var n2 = t2, i2 = r2, s2 = e2[o2];
        null != s2 && (jn(n2, i2, 0), Dn(n2.h, s2));
      }
    }, Qn);
    ds = $i(Zi, function(t2, e2, r2) {
      if (null != (e2 = Mi(re, e2, true)) && e2.length) {
        r2 = Gn(t2, r2);
        for (let r3 = 0; r3 < e2.length; r3++) Dn(t2.h, e2[r3]);
        Vn(t2, r2);
      }
    }, Qn);
    fs = Pi(function(t2, e2, r2) {
      return 0 === t2.i && (Ui(e2, r2, 0 === (t2 = Yr(t2.h)) ? void 0 : t2), true);
    }, Vi, Qn);
    ps = Pi(function(t2, e2, r2) {
      return 0 === t2.i && (Ui(e2, r2, Wr(t2.h)), true);
    }, Hi, Jn);
    ms = Pi(function(t2, e2, r2) {
      return 0 === t2.i && (Ui(e2, r2, false === (t2 = Wr(t2.h)) ? void 0 : t2), true);
    }, Hi, Jn);
    gs = $i(function(t2, e2, r2) {
      return 2 === t2.i && (t2 = fn(t2), gr(e2, 0 | e2[tt], r2).push(t2), true);
    }, function(t2, e2, r2) {
      if (null != (e2 = Mi(ve, e2, true))) for (let o2 = 0; o2 < e2.length; o2++) {
        var n2 = t2, i2 = r2, s2 = e2[o2];
        null != s2 && Kn(n2, i2, l(s2));
      }
    }, Zn);
    vs = Pi(function(t2, e2, r2) {
      return 2 === t2.i && (Ui(e2, r2, "" === (t2 = fn(t2)) ? void 0 : t2), true);
    }, Ki, Zn);
    _s = Pi(function(t2, e2, r2) {
      return 2 === t2.i && (Ui(e2, r2, fn(t2)), true);
    }, Ki, Zn);
    ys = (function(t2, e2, r2 = qn) {
      return new ci(t2, e2, r2);
    })(function(t2, e2, r2, n2, i2) {
      return 2 === t2.i && (n2 = Ve(void 0, n2), gr(e2, 0 | e2[tt], r2).push(n2), dn(t2, n2, i2), true);
    }, function(t2, e2, r2, n2, i2) {
      if (Array.isArray(e2)) {
        for (let s2 = 0; s2 < e2.length; s2++) Xi(t2, e2[s2], r2, n2, i2);
        1 & (t2 = 0 | e2[tt]) || nt(e2, 1 | t2);
      }
    });
    Es = li(function(t2, e2, r2, n2, i2, s2) {
      if (2 !== t2.i) return false;
      var o2 = 0 | e2[tt];
      return yr(e2, o2, s2, r2, mt(o2)), dn(t2, e2 = Ar(e2, n2, r2), i2), true;
    }, Xi);
    As = Pi(function(t2, e2, r2) {
      return 2 === t2.i && (Ui(e2, r2, pn(t2)), true);
    }, Wi, oi);
    ws = $i(function(t2, e2, r2) {
      return 2 === t2.i && (t2 = pn(t2), gr(e2, 0 | e2[tt], r2).push(t2), true);
    }, function(t2, e2, r2) {
      if (null != (e2 = Mi(pe, e2, false))) for (let o2 = 0; o2 < e2.length; o2++) {
        var n2 = t2, i2 = r2, s2 = e2[o2];
        null != s2 && Kn(n2, i2, Kr(s2, true).buffer);
      }
    }, oi);
    Ts = Pi(function(t2, e2, r2) {
      return 0 === t2.i && (Ui(e2, r2, zr(t2.h)), true);
    }, Yi, ti);
    bs = $i(function(t2, e2, r2) {
      return (0 === t2.i || 2 === t2.i) && (e2 = gr(e2, 0 | e2[tt], r2), 2 == t2.i ? mn(t2, zr, e2) : e2.push(zr(t2.h)), true);
    }, function(t2, e2, r2) {
      if (null != (e2 = Mi(ne, e2, true))) for (let o2 = 0; o2 < e2.length; o2++) {
        var n2 = t2, i2 = r2, s2 = e2[o2];
        null != s2 && (jn(n2, i2, 0), Mn(n2.h, s2));
      }
    }, ti);
    ks = Pi(function(t2, e2, r2) {
      return 0 === t2.i && (Ui(e2, r2, 0 === (t2 = zr(t2.h)) ? void 0 : t2), true);
    }, Yi, ti);
    Ss = Pi(function(t2, e2, r2) {
      return 0 === t2.i && (Ui(e2, r2, Yr(t2.h)), true);
    }, zi, ai);
    Ls = Pi(function(t2, e2, r2) {
      return 0 === t2.i && (Ui(e2, r2, 0 === (t2 = Yr(t2.h)) ? void 0 : t2), true);
    }, zi, ai);
    xs = Pi(function(t2, e2, r2) {
      return 0 !== t2.i ? t2 = false : (Ui(e2, r2, (function(t3) {
        return Xr(t3, (t4, e3) => {
          var r3 = -(1 & t4);
          return Pt(t4 = (t4 >>> 1 | e3 << 31) ^ r3, e3 >>> 1 ^ r3);
        });
      })(t2.h)), t2 = true), t2;
    }, function(t2, e2, r2) {
      if (null != (e2 = de(e2)) && (Yn(e2), null != e2)) switch (jn(t2, r2, 0), typeof e2) {
        case "number":
          t2 = t2.h, e2 = (r2 = e2) < 0, Ct(r2 = 2 * Math.abs(r2)), r2 = It;
          let n2 = Ft;
          e2 && (0 == r2 ? 0 == n2 ? n2 = r2 = 4294967295 : (n2--, r2 = 4294967295) : r2--), On(t2, It = r2, Ft = n2);
          break;
        case "bigint":
          t2 = t2.h, e2 = e2 << BigInt(1) ^ e2 >> BigInt(63), It = Number(BigInt.asUintN(32, e2)), Ft = Number(BigInt.asUintN(32, e2 >> BigInt(32))), On(t2, It, Ft);
          break;
        default:
          Pn(t2.h, e2);
      }
    }, ni);
    Rs = class {
      constructor(t2, e2) {
        var r2 = no;
        this.h = t2, this.i = e2, this.o = Tr, this.u = Lr, this.defaultValue = void 0, this.l = null != r2.ib ? pt : void 0;
      }
      register() {
        v(this);
      }
    };
    Os = [0, As, ws, ps, _s];
    Ms = [0, vs, [0, Ls, [0, hs, fs], Ls, -1, [0, Ss], Ls, -1], Pi(Qi, Wi, oi)];
    Ps = class extends ui {
      constructor(t2) {
        super(t2);
      }
    };
    $s = [0, vs, Pi(Qi, function(t2, e2, r2) {
      if (null != e2) {
        if (e2 instanceof ui) {
          let n2 = e2.mb;
          return void (n2 ? (e2 = n2(e2), null != e2 && Kn(t2, r2, Kr(e2, true).buffer)) : $(z, 3));
        }
        if (Array.isArray(e2)) return void $(z, 3);
      }
      Wi(t2, e2, r2);
    }, oi)];
    Us = [0, 1, [0, 12, cs, 10, ps], [0, 7, [0, cs, -1]]];
    Bs = globalThis.trustedTypes;
    js = class {
      constructor(t2) {
        this.h = t2;
      }
      toString() {
        return this.h + "";
      }
    };
    Hs = [0, cs, Ss, ps, -1, ds, Ss, -1, ps, -1];
    Ks = [0, Ss, -1, ps];
    Xs = class extends ui {
      constructor(t2) {
        super(t2);
      }
    };
    Ws = [0, ps, _s, ps, Ss, -1, $i(function(t2, e2, r2) {
      return (0 === t2.i || 2 === t2.i) && (e2 = gr(e2, 0 | e2[tt], r2), 2 == t2.i ? mn(t2, Qr, e2) : e2.push(Yr(t2.h)), true);
    }, function(t2, e2, r2) {
      if (null != (e2 = Mi(re, e2, true)) && e2.length) {
        r2 = Gn(t2, r2);
        for (let r3 = 0; r3 < e2.length; r3++) Dn(t2.h, e2[r3]);
        Vn(t2, r2);
      }
    }, ai), _s, -1, [0, ps, -1], Ss, ps, -1, Ks];
    Ys = class extends ui {
      constructor(t2) {
        super(t2);
      }
    };
    zs = class extends ui {
      constructor(t2) {
        super(t2);
      }
    };
    qs = class extends ui {
      constructor(t2) {
        super(t2);
      }
      h() {
        return ar(this, 9);
      }
    };
    Js = [0, 3, ps, -1, 2, [0, [2], cs, Es, [0, Ts]], [0, Ss, ps, Ss, ps, Ss, 4, [0, ps, _s, -1, ps, cs, -1, ps], Ss, ps, -4, Ss, gs, ps, gs, ps, -1, cs, ps, -3, [0, ps, cs], [0, _s, cs, -1], [0, ps], [0, ps, -1], [0, ps, cs, -1]], [0, [3, 4], _s, -1, Es, [0, cs], Es, [0, Ss, -2]], [0], _s, ps];
    Zs = [0, _s, -2];
    Qs = class extends ui {
      constructor(t2) {
        super(t2);
      }
    };
    to = [0];
    eo = class extends ui {
      constructor(t2) {
        super(t2);
      }
    };
    ro = [0, cs, ps, 1, ps, -4];
    no = class extends ui {
      constructor(t2) {
        super(t2, 2);
      }
    };
    io = {};
    io[336783863] = [0, _s, ps, -1, cs, [0, [1, 2, 3, 4, 5, 6, 7, 8, 9], Es, to, Es, Ws, Es, Zs, Es, ro, Es, Hs, Es, [0, _s, -2], Es, [0, _s, Ss], Es, Js, Es, Ks], [0, _s], ps, [0, [1, 3], [2, 4], Es, [0, ds], -1, Es, [0, gs], -1, ys, [0, _s, -1]], _s];
    so = [0, hs, -1, ms, -3, hs, ds, vs, fs, hs, -1, ms, fs, ms, -2, vs];
    ho = class extends ui {
      constructor(t2) {
        super(t2, 500);
      }
      A(t2) {
        return Lr(this, 0, 7, t2);
      }
    };
    uo = [-1, {}];
    co = [0, _s, 1, uo];
    lo = [0, _s, gs, uo];
    go = class extends ui {
      constructor(t2) {
        super(t2, 500);
      }
      A(t2) {
        return Lr(this, 0, 1001, t2);
      }
    };
    vo = [-500, ys, [-500, vs, -1, gs, -3, [-2, io, ps], ys, $s, fs, -1, co, lo, ys, [0, vs, ms], vs, so, fs, gs, 987, gs], 4, ys, [-500, _s, -1, [-1, {}], 998, _s], ys, [-500, _s, gs, -1, [-2, {}, ps], 997, gs, -1], fs, ys, [-500, _s, gs, uo, 998, gs], gs, fs, co, lo, ys, [0, vs, -1, uo], gs, -2, so, vs, -1, ms, [0, ms, ks], 978, uo, ys, $s];
    go.prototype.h = Ns(vo);
    _o = Fs(go, vo);
    yo = class extends ui {
      constructor(t2) {
        super(t2);
      }
    };
    Eo = class extends ui {
      constructor(t2) {
        super(t2);
      }
      h() {
        return kr(this, yo, 1);
      }
    };
    Ao = [0, ys, [0, cs, es, _s, -1]];
    wo = Fs(Eo, Ao);
    To = class extends ui {
      constructor(t2) {
        super(t2);
      }
    };
    bo = class extends ui {
      constructor(t2) {
        super(t2);
      }
    };
    ko = class extends ui {
      constructor(t2) {
        super(t2);
      }
      l() {
        return Tr(this, To, 2);
      }
      h() {
        return kr(this, bo, 5);
      }
    };
    So = Fs(class extends ui {
      constructor(t2) {
        super(t2);
      }
    }, [0, gs, ds, ns, [0, Ss, [0, cs, -3], [0, es, -3], [0, cs, -1, [0, ys, [0, cs, -2]]], ys, [0, es, -1, _s, es]], _s, -1, os, ys, [0, cs, es], gs, os]);
    Lo = class extends ui {
      constructor(t2) {
        super(t2);
      }
    };
    xo = Fs(class extends ui {
      constructor(t2) {
        super(t2);
      }
    }, [0, ys, [0, es, -4]]);
    Ro = class extends ui {
      constructor(t2) {
        super(t2);
      }
    };
    Io = Fs(class extends ui {
      constructor(t2) {
        super(t2);
      }
    }, [0, ys, [0, es, -4]]);
    Fo = class extends ui {
      constructor(t2) {
        super(t2);
      }
    };
    Co = [0, cs, -1, ns, Ss];
    No = class extends ui {
      constructor(t2) {
        super(t2);
      }
    };
    No.prototype.h = Ns([0, es, -4, os]);
    Oo = class extends ui {
      constructor(t2) {
        super(t2);
      }
    };
    Mo = Fs(class extends ui {
      constructor(t2) {
        super(t2);
      }
    }, [0, ys, [0, 1, cs, _s, Ao], os]);
    Do = class extends ui {
      constructor(t2) {
        super(t2);
      }
    };
    Po = class extends ui {
      constructor(t2) {
        super(t2);
      }
      h() {
        var t2 = nr(this, 1, void 0, fr);
        return null == t2 ? I() : t2;
      }
    };
    $o = class extends ui {
      constructor(t2) {
        super(t2);
      }
    };
    Uo = [1, 2];
    Bo = Fs(class extends ui {
      constructor(t2) {
        super(t2);
      }
    }, [0, ys, [0, Uo, Es, [0, ns], Es, [0, As], cs, _s], os]);
    jo = class extends ui {
      constructor(t2) {
        super(t2);
      }
    };
    Go = [0, _s, cs, es, gs, -1];
    Vo = class extends ui {
      constructor(t2) {
        super(t2);
      }
    };
    Ho = [0, ps, -1];
    Ko = class extends ui {
      constructor(t2) {
        super(t2);
      }
      h() {
        return hr(this, 2, Xo);
      }
      l() {
        return Or(this, qs, 6, Xo);
      }
    };
    Xo = [1, 2, 3, 4, 5, 6];
    Wo = class extends ui {
      constructor(t2) {
        super(t2);
      }
      h() {
        return null != nr(this, 1, void 0, fr);
      }
      l() {
        return null != ve(nr(this, 2));
      }
    };
    Yo = class extends ui {
      constructor(t2) {
        super(t2);
      }
    };
    zo = [0, As, _s, [0, cs, os, -1], [0, us, os]];
    qo = [0, zo, ps, [0, Xo, Es, ro, Es, Ws, Es, Hs, Es, to, Es, Zs, Es, Js], Ss];
    Jo = Cs(qo);
    Zo = class extends ui {
      constructor(t2) {
        super(t2);
      }
    };
    Qo = [0, qo, es, -1, cs];
    ta = Is(502141897, Zo);
    io[502141897] = Qo;
    ea = Fs(class extends ui {
      constructor(t2) {
        super(t2);
      }
    }, [0, [0, Ss, -1, rs, bs], Co]);
    ra = class extends ui {
      constructor(t2) {
        super(t2);
      }
    };
    na = class extends ui {
      constructor(t2) {
        super(t2);
      }
    };
    ia = [0, qo, es, [0, qo], ps];
    sa = Is(508968150, na);
    io[508968150] = [0, qo, Qo, ia, es, [0, [0, zo]]], io[508968149] = ia;
    oa = class extends ui {
      constructor(t2) {
        super(t2);
      }
      l() {
        return Tr(this, jo, 2);
      }
      h() {
        sr(this, 2);
      }
    };
    aa = [0, qo, Go];
    io[478825465] = aa;
    ha = class extends ui {
      constructor(t2) {
        super(t2);
      }
    };
    ua = class extends ui {
      constructor(t2) {
        super(t2);
      }
    };
    ca = class extends ui {
      constructor(t2) {
        super(t2);
      }
    };
    la = class extends ui {
      constructor(t2) {
        super(t2);
      }
    };
    da = class extends ui {
      constructor(t2) {
        super(t2);
      }
    };
    fa = [0, qo, [0, qo], aa, -1];
    pa = [0, qo, es, cs];
    ma = [0, qo, es];
    ga = [0, qo, pa, ma, es];
    va = Is(479097054, da);
    io[479097054] = [0, qo, ga, fa], io[463370452] = fa, io[464864288] = pa;
    _a = Is(462713202, la);
    io[462713202] = ga, io[474472470] = ma;
    ya = class extends ui {
      constructor(t2) {
        super(t2);
      }
    };
    Ea = class extends ui {
      constructor(t2) {
        super(t2);
      }
    };
    Aa = class extends ui {
      constructor(t2) {
        super(t2);
      }
    };
    wa = class extends ui {
      constructor(t2) {
        super(t2);
      }
    };
    Ta = [0, qo, es, -1, cs];
    ba = [0, qo, es, ps];
    wa.prototype.h = Ns([0, qo, ma, [0, qo], Qo, ia, Ta, ba]);
    ka = class extends ui {
      constructor(t2) {
        super(t2);
      }
    };
    Sa = Is(456383383, ka);
    io[456383383] = [0, qo, Go];
    La = class extends ui {
      constructor(t2) {
        super(t2);
      }
    };
    xa = Is(476348187, La);
    io[476348187] = [0, qo, Ho];
    Ra = class extends ui {
      constructor(t2) {
        super(t2);
      }
    };
    Ia = class extends ui {
      constructor(t2) {
        super(t2);
      }
    };
    Fa = [0, Ss, -1];
    Ca = Is(458105876, class extends ui {
      constructor(t2) {
        super(t2);
      }
      h() {
        var t2 = this.B, e2 = 0 | t2[tt], r2 = ut(this, e2);
        return t2 = (function(t3, e3, r3, n2) {
          var i2 = Ia;
          !n2 && Je(t3) && (r3 = 0 | (e3 = t3.B)[tt]);
          var s2 = ir(e3, 2);
          if (t3 = false, null == s2) {
            if (n2) return Fe();
            s2 = [];
          } else if (s2.constructor === xe) {
            if (!(2 & s2.N) || n2) return s2;
            s2 = s2.fa();
          } else Array.isArray(s2) ? t3 = !!(2 & s2[tt]) : s2 = [];
          if (n2) {
            if (!s2.length) return Fe();
            t3 || (t3 = true, it(s2));
          } else t3 && (t3 = false, st(s2), s2 = pr(s2));
          return !t3 && 32 & r3 && rt(s2, 32), r3 = or(e3, r3, 2, n2 = new xe(s2, i2, ye, void 0)), t3 || Qe(e3, r3), n2;
        })(this, t2, e2, r2), !r2 && Ia && (t2.Fa = true), t2;
      }
    });
    io[458105876] = [0, Fa, Bi, [true, os, [0, _s, -1, gs]], [0, ds, ps, Ss], ps];
    Na = class extends ui {
      constructor(t2) {
        super(t2);
      }
    };
    Oa = Is(458105758, Na);
    io[458105758] = [0, qo, _s, Fa];
    Ma = class extends ui {
      constructor(t2) {
        super(t2);
      }
    };
    Da = class extends ui {
      constructor(t2) {
        super(t2);
      }
    };
    Pa = class extends ui {
      constructor(t2) {
        super(t2);
      }
    };
    $a = Cs([0, ys, [0, Ls, ys, [0, is, -1], ms]]);
    Ua = class extends ui {
      constructor(t2) {
        super(t2);
      }
    };
    Ba = [0, is, -1, ms];
    ja = class extends ui {
      constructor(t2) {
        super(t2);
      }
    };
    Ga = class extends ui {
      constructor(t2) {
        super(t2);
      }
    };
    Va = [1, 2];
    Ga.prototype.h = Ns([0, Va, Es, Ba, Es, [0, ys, Ba]]);
    Ha = class extends ui {
      constructor(t2) {
        super(t2);
      }
    };
    Ka = Is(443442058, Ha);
    io[443442058] = [0, qo, _s, cs, es, gs, -1, ps, es], io[514774813] = Ta;
    Xa = class extends ui {
      constructor(t2) {
        super(t2);
      }
    };
    Wa = Is(516587230, Xa);
    io[516587230] = [0, qo, Ta, ba, es], io[518928384] = ba;
    ih = class {
    };
    ih.forVisionTasks = function(t2, e2 = false) {
      return nh("vision", t2 ?? Vs``, e2);
    }, ih.forLiteRtLmTextTasks = function(t2, e2 = false) {
      return nh("litertlm_text", t2 ?? Vs``, e2);
    }, ih.forTextTasks = function(t2, e2 = false) {
      return nh("text", t2 ?? Vs``, e2);
    }, ih.forRetrievalTasks = function(t2, e2 = false) {
      return nh("retrieval", t2 ?? Vs``, e2);
    }, ih.forGenAiTasks = function(t2, e2 = false) {
      return nh("genai", t2 ?? Vs``, e2);
    }, ih.forDecisionTasks = function(t2, e2 = false) {
      return nh("decision", t2 ?? Vs``, e2);
    }, ih.forAudioTasks = function(t2, e2 = false) {
      return nh("audio", t2 ?? Vs``, e2);
    };
    oh = class extends ui {
      constructor(t2) {
        super(t2);
      }
    };
    ah = class extends ui {
      constructor(t2) {
        super(t2);
      }
    };
    hh = [0, Ss, 2, us, -2, os, ys, [0, Ss, os]];
    uh = class extends ui {
      constructor(t2) {
        super(t2);
      }
    };
    ch = class extends ui {
      constructor(t2) {
        super(t2);
      }
    };
    fh = class extends ui {
      constructor(t2) {
        super(t2);
      }
    };
    ph = [3, 4, 5, 6, 7];
    mh = class extends ui {
      constructor(t2) {
        super(t2);
      }
    };
    gh = class extends ui {
      constructor(t2) {
        super(t2);
      }
    };
    gh.prototype.h = Ns([0, [0, Ss, _s, -3, Ss], [0, ph, Ss, -1, Es, [0, Ss, _s, us], Es, hh, Es, [0, 1, hh], Es, [0, Ss], Es, [0, Ss, _s, us]]]);
    vh = class {
      constructor() {
        this.h = "undefined" != typeof AbortController;
      }
      async send(t2, e2, r2) {
        var n2 = this.h ? new AbortController() : void 0, i2 = n2 && t2.la > 0 ? setTimeout(() => {
          n2.abort();
        }, t2.la) : void 0;
        try {
          let i3 = await fetch(t2.url, { method: t2.bb, headers: { ...t2.ab }, ...t2.body && { body: t2.body }, ...t2.withCredentials && { credentials: "include" }, signal: t2.la && n2 ? n2.signal : null });
          i3.ok ? e2?.(await i3.text()) : r2?.(i3.status);
        } catch (t3) {
          if ("AbortError" === t3?.name) r2?.(408);
          else r2?.(0);
        } finally {
          clearTimeout(i2);
        }
      }
    };
    _h = class extends ui {
      constructor(t2) {
        super(t2, 37);
      }
    };
    yh = [-4, {}, Us, Ss, Ms];
    Eh = [0, _s, Ss, 1, _s, -1, Ss, 1, Ss, 1, os];
    Ah = [0, Ss, _s, -2];
    wh = [0, _s, Ss];
    Th = [0, _s, Ss];
    bh = [0, ps, -3];
    kh = [0, Ss, _s, -1, os, cs, -1, _s, -5, ys, [0, _s, -4], -1, ps, [0, ps, -3], Ss];
    Sh = class extends ui {
      constructor(t2) {
        super(t2, 19);
      }
    };
    Lh = Cs([-19, {}, [0, Ss, 1, [0, _s, -6, os, cs, _s, -1, os], 1, [0, _s, 1, _s, -5], _s, -1, [0, Ss, _s, -8], [0, _s, -3], [0, _s, Ss, _s, -2], [0, _s, -1, Ss, _s, -1, Ss, _s, -1, [0, ys, [0, _s, -1], ps, _s, -5], [0, Ss, ps, cs, -2]], os, [0, _s, -3, os, cs, _s, -1], [0, Ss, _s, -1], [0, _s, -9], [0, _s, -6, Ss, _s, 1, _s, ps, Ss, -1, ps, _s, -2, Ss, _s, Ss, _s, cs, -1], 1, [0, Ss], 1, [0, _s, -4], 1, Eh, [0, [1, 2, 3, 4, 5, 6], Es, Eh, Es, wh, Es, Th, Es, [0, Ss], Es, kh, Es, Ah], wh, Th, kh, [0, [0, Ss, _s, -1, os, cs, -1, _s, -4, ys, [0, _s, -4], -1, 1, bh], [0, Ss, _s, -1, os, cs, -1, _s, -4, bh]], Ah, [0, _s, [0, cs, -3, Ss], Ss, -2, [0, cs, -1], ps], 4, [0, _s, Ss, _s, -1, os, Ss, _s, -1, Ss, cs, -1]], Ss, ys, [-37, {}, ss, _s, ys, [0, _s, -1], As, 1, As, [0, gs, -1, ls, as, -1], _s, [0, cs, _s, -1], ps, cs, os, _s, -1, xs, Os, ss, As, Ss, ls, os, -1, [0, Ss, -1], _s, ps, _s, ds, _s, -1, ts, 1, ts, yh, ps, [0, Ss, [0, es, cs, -2], [0, es]], [0, Ss, os]], ss, ws, _s, -1, ss, Ss, -1, [0, ps, -1, Ss, ps], [0, os, -1, _s], [0, ss, ps, os], os, 1, Ts, 1, yh]);
    xh = class {
      constructor(t2) {
        this.i = [], this.o = new vh(), this.l = t2 ?? "", this.h = setInterval(() => {
          this.flush();
        }, 6e4);
      }
      close() {
        void 0 !== this.h && (clearInterval(this.h), this.h = void 0), this.flush();
      }
      flush(t2, e2) {
        if (this.error) e2?.("net-send-failed");
        else if (0 === this.i.length) t2?.();
        else {
          var r2 = this.i;
          this.i = [], r2 = (function(t3) {
            var e3 = new Sh();
            return Rr(e3 = sr(e3, 2, ee(1786)), 3, t3);
          })(r2), r2 = Lh(r2), this.o.send({ url: "https://odml.pa.googleapis.com/v1/log", bb: "POST", la: 1e4, body: r2, gb: 2, ab: { "Content-Type": "application/x-protobuf", "x-goog-api-key": this.l }, withCredentials: false }, () => {
            t2?.();
          }, (t3) => {
            this.error = Error(`Logging failed with HTTP error: ${t3}`), this.i = [], void 0 !== this.h && (clearInterval(this.h), this.h = void 0), e2?.("net-send-failed", t3);
          });
        }
      }
    };
    Rh = class {
      constructor() {
        this.ba = this.V = this.Y = this.S = this.W = this.U = this.R = 0;
      }
    };
    Nh = class {
      constructor(t2, e2, r2) {
        this.v = performance.now(), this.o = performance.now(), this.i = /* @__PURE__ */ new Map(), this.u = 0, this.h = new Rh(), this.l = new Rh(), this.m = new xh(r2), this.D = (function(t3) {
          switch (t3) {
            case "AudioClassifier":
              return 4;
            case "AudioEmbedder":
              return 5;
            case "TextClassifier":
              return 6;
            case "TextEmbedder":
              return 7;
            case "GestureRecognizer":
              return 8;
            case "HandDetector":
              return 9;
            case "HandLandmarker":
              return 10;
            case "ImageClassifier":
              return 11;
            case "ImageEmbedder":
              return 12;
            case "ImageSegmenter":
              return 13;
            case "ObjectDetector":
              return 14;
            case "FaceDetector":
              return 15;
            case "FaceLandmarker":
              return 16;
            case "InteractiveSegmenter":
            case "InteractiveSegmenterLegacy":
              return 18;
            case "HolisticLandmarker":
              return 20;
            case "LlmInference":
              return 21;
            case "LanguageDetector":
              return 22;
            case "PoseLandmarker":
              return 23;
            case "UniversalEmbedder":
              return 28;
            case "SemanticRetriever":
              return 29;
            case "DecisionMaker":
              return 30;
            case "TextSummarizer":
              return 26;
            case "TextProofreader":
              return 27;
            case "AudioTranscriber":
              return 31;
            case "TextToSpeech":
              return 32;
            default:
              return 0;
          }
        })(t2), this.F = (function(t3) {
          switch (t3) {
            case "IMAGE":
              return 11;
            case "VIDEO":
              return 12;
            case "LIVE_STREAM":
              return 13;
            case "AUDIO_CLIPS":
              return 14;
            case "AUDIO_STREAM":
              return 15;
            default:
              return 10;
          }
        })(e2), t2 = new mh(), "undefined" == typeof navigator ? e2 = 0 : (e2 = navigator.userAgent, e2 = /Android/i.test(e2) ? 1 : /iPhone|iPad|iPod/i.test(e2) ? 2 : /Windows/i.test(e2) ? 5 : /Macintosh/i.test(e2) ? 4 : /Linux/i.test(e2) ? 3 : 0), t2 = sr(t2, 1, ee(e2)), t2 = sr(t2, 2, ge("")), t2 = sr(t2, 3, ge("")), t2 = sr(t2, 4, ge("1.2.3")), t2 = sr(t2, 5, ge("")), this.C = sr(t2, 6, ee((function() {
          var t3 = globalThis.__HOST_ENVIRONMENT__;
          if ("string" == typeof t3) switch (t3) {
            case "ANDROID":
              return 1;
            case "IOS":
              return 2;
            case "PYTHON":
              return 3;
            case "WEB":
              break;
            case "FLUTTER":
              return 5;
            case "FLUTTER_WEB":
              return 6;
            default:
              return 0;
          }
          return 4;
        })()));
      }
      ya() {
        var t2 = new ch();
        t2 = $r(t2 = sr(t2, 1, ee(this.F)), 3, performance.now() - this.v), Ih(this, t2 = xr(dh(lh(new fh(), this.D), 0), 3, ph, t2)), this.o = performance.now();
      }
      za(t2) {
        var e2 = this.i.get(t2);
        if (void 0 !== e2 && (this.i.delete(t2), t2 = performance.now() - e2, ++this.h.W, this.h.Y += t2, this.h.V = Math.max(this.h.V, t2), this.u = Math.max(this.u, t2), performance.now() > this.o + 3e4)) {
          for (let [r2, n2] of this.i.entries()) t2 = r2, n2 < e2 && (this.h.S++, this.i.delete(t2));
          e2 = { ...this.h, ba: performance.now() - this.o }, this.h.V = 0, this.o = performance.now(), Fh(this, e2);
        }
      }
      xa() {
        var t2 = { ...this.h, S: this.h.S + this.i.size, V: this.u, ba: performance.now() - this.v };
        Fh(this, t2);
        var e2 = new uh();
        e2 = Lr(e2, 0, 2, t2 = Ch(this, t2)), Ih(this, e2 = xr(t2 = dh(lh(new fh(), this.D), 2), 5, ph, e2));
      }
      close() {
        var t2 = this.m;
        "function" == typeof t2.close ? t2.close() : t2.flush();
      }
    };
    Gh = class {
      constructor(t2, e2) {
        this.l = true, this.j = t2, this.h = null, this.i = 0, this.o = "function" == typeof this.j._addIntToInputStream, void 0 !== e2 ? this.j.canvas = e2 : Oh() ? this.j.canvas = new OffscreenCanvas(1, 1) : (console.warn("OffscreenCanvas not supported and GraphRunner constructor glCanvas parameter is undefined. Creating backup canvas."), this.j.canvas = document.createElement("canvas"));
      }
      async initializeGraph(t2) {
        var e2 = await (await fetch(t2)).arrayBuffer();
        t2 = !(t2.endsWith(".pbtxt") || t2.endsWith(".textproto")), this.setGraph(new Uint8Array(e2), t2);
      }
      setGraphFromString(t2) {
        this.setGraph(new TextEncoder().encode(t2), false);
      }
      setGraph(t2, e2) {
        var r2 = t2.length, n2 = this.j._malloc(r2);
        this.j.HEAPU8.set(t2, n2), e2 ? this.j._changeBinaryGraph(r2, n2) : this.j._changeTextGraph(r2, n2), this.j._free(n2);
      }
      configureAudio(t2, e2, r2, n2, i2) {
        this.j._configureAudio || console.warn('Attempting to use configureAudio without support for input audio. Is build dep ":gl_graph_runner_audio" missing?'), Ph(this, n2 || "input_audio", (n3) => {
          Ph(this, i2 = i2 || "audio_header", (i3) => {
            this.j._configureAudio(n3, i3, t2, e2 ?? 0, r2);
          });
        });
      }
      setAutoResizeCanvas(t2) {
        this.l = t2;
      }
      setAutoRenderToScreen(t2) {
        this.j._setAutoRenderToScreen(t2);
      }
      setGpuBufferVerticalFlip(t2) {
        this.j.gpuOriginForWebTexturesIsBottomLeft = t2;
      }
      ka(t2) {
        Bh(this, "__graph_config__", (e2) => {
          t2(e2);
        }), Ph(this, "__graph_config__", (t3) => {
          this.j._getGraphConfig(t3, void 0);
        }), delete this.j.simpleListeners.__graph_config__;
      }
      attachErrorListener(t2) {
        this.j.errorListener = t2;
      }
      attachEmptyPacketListener(t2, e2) {
        this.j.emptyPacketListeners = this.j.emptyPacketListeners || {}, this.j.emptyPacketListeners[t2] = e2;
      }
      addAudioToStream(t2, e2, r2) {
        this.addAudioToStreamWithShape(t2, 0, 0, e2, r2);
      }
      addAudioToStreamWithShape(t2, e2, r2, n2, i2) {
        var s2 = 4 * t2.length;
        this.i !== s2 && (this.h && this.j._free(this.h), this.h = this.j._malloc(s2), this.i = s2), this.j.HEAPF32.set(t2, this.h / 4), Ph(this, n2, (t3) => {
          this.j._addAudioToInputStream(this.h, e2, r2, t3, i2);
        });
      }
      addGpuBufferToStream(t2, e2, r2) {
        Ph(this, e2, (e3) => {
          var [n2, i2] = $h(this, t2, e3);
          this.j._addBoundTextureToStream(e3, n2, i2, r2);
        });
      }
      addBoolToStream(t2, e2, r2) {
        Ph(this, e2, (e3) => {
          this.j._addBoolToInputStream(t2, e3, r2);
        });
      }
      addDoubleToStream(t2, e2, r2) {
        Ph(this, e2, (e3) => {
          this.j._addDoubleToInputStream(t2, e3, r2);
        });
      }
      addFloatToStream(t2, e2, r2) {
        Ph(this, e2, (e3) => {
          this.j._addFloatToInputStream(t2, e3, r2);
        });
      }
      addIntToStream(t2, e2, r2) {
        Ph(this, e2, (e3) => {
          this.j._addIntToInputStream(t2, e3, r2);
        });
      }
      addUintToStream(t2, e2, r2) {
        Ph(this, e2, (e3) => {
          this.j._addUintToInputStream(t2, e3, r2);
        });
      }
      addStringToStream(t2, e2, r2) {
        Ph(this, e2, (e3) => {
          Ph(this, t2, (t3) => {
            this.j._addStringToInputStream(t3, e3, r2);
          });
        });
      }
      addStringRecordToStream(t2, e2, r2) {
        Ph(this, e2, (e3) => {
          Uh(this, Object.keys(t2), (n2) => {
            Uh(this, Object.values(t2), (i2) => {
              this.j._addFlatHashMapToInputStream(n2, i2, Object.keys(t2).length, e3, r2);
            });
          });
        });
      }
      addProtoToStream(t2, e2, r2, n2) {
        Ph(this, r2, (r3) => {
          Ph(this, e2, (e3) => {
            var i2 = this.j._malloc(t2.length);
            this.j.HEAPU8.set(t2, i2), this.j._addProtoToInputStream(i2, t2.length, e3, r3, n2), this.j._free(i2);
          });
        });
      }
      addEmptyPacketToStream(t2, e2) {
        Ph(this, t2, (t3) => {
          this.j._addEmptyPacketToInputStream(t3, e2);
        });
      }
      addBoolVectorToStream(t2, e2, r2) {
        Ph(this, e2, (e3) => {
          var n2 = this.j._allocateBoolVector(t2.length);
          if (!n2) throw Error("Unable to allocate new bool vector on heap.");
          for (let e4 of t2) this.j._addBoolVectorEntry(n2, e4);
          this.j._addBoolVectorToInputStream(n2, e3, r2);
        });
      }
      addDoubleVectorToStream(t2, e2, r2) {
        Ph(this, e2, (e3) => {
          var n2 = this.j._allocateDoubleVector(t2.length);
          if (!n2) throw Error("Unable to allocate new double vector on heap.");
          for (let e4 of t2) this.j._addDoubleVectorEntry(n2, e4);
          this.j._addDoubleVectorToInputStream(n2, e3, r2);
        });
      }
      addFloatVectorToStream(t2, e2, r2) {
        Ph(this, e2, (e3) => {
          var n2 = this.j._allocateFloatVector(t2.length);
          if (!n2) throw Error("Unable to allocate new float vector on heap.");
          for (let e4 of t2) this.j._addFloatVectorEntry(n2, e4);
          this.j._addFloatVectorToInputStream(n2, e3, r2);
        });
      }
      addIntVectorToStream(t2, e2, r2) {
        Ph(this, e2, (e3) => {
          var n2 = this.j._allocateIntVector(t2.length);
          if (!n2) throw Error("Unable to allocate new int vector on heap.");
          for (let e4 of t2) this.j._addIntVectorEntry(n2, e4);
          this.j._addIntVectorToInputStream(n2, e3, r2);
        });
      }
      addUintVectorToStream(t2, e2, r2) {
        Ph(this, e2, (e3) => {
          var n2 = this.j._allocateUintVector(t2.length);
          if (!n2) throw Error("Unable to allocate new unsigned int vector on heap.");
          for (let e4 of t2) this.j._addUintVectorEntry(n2, e4);
          this.j._addUintVectorToInputStream(n2, e3, r2);
        });
      }
      addStringVectorToStream(t2, e2, r2) {
        Ph(this, e2, (e3) => {
          var n2 = this.j._allocateStringVector(t2.length);
          if (!n2) throw Error("Unable to allocate new string vector on heap.");
          for (let e4 of t2) Ph(this, e4, (t3) => {
            this.j._addStringVectorEntry(n2, t3);
          });
          this.j._addStringVectorToInputStream(n2, e3, r2);
        });
      }
      addBoolToInputSidePacket(t2, e2) {
        Ph(this, e2, (e3) => {
          this.j._addBoolToInputSidePacket(t2, e3);
        });
      }
      addDoubleToInputSidePacket(t2, e2) {
        Ph(this, e2, (e3) => {
          this.j._addDoubleToInputSidePacket(t2, e3);
        });
      }
      addFloatToInputSidePacket(t2, e2) {
        Ph(this, e2, (e3) => {
          this.j._addFloatToInputSidePacket(t2, e3);
        });
      }
      addIntToInputSidePacket(t2, e2) {
        Ph(this, e2, (e3) => {
          this.j._addIntToInputSidePacket(t2, e3);
        });
      }
      addUintToInputSidePacket(t2, e2) {
        Ph(this, e2, (e3) => {
          this.j._addUintToInputSidePacket(t2, e3);
        });
      }
      addStringToInputSidePacket(t2, e2) {
        Ph(this, e2, (e3) => {
          Ph(this, t2, (t3) => {
            this.j._addStringToInputSidePacket(t3, e3);
          });
        });
      }
      addProtoToInputSidePacket(t2, e2, r2) {
        Ph(this, r2, (r3) => {
          Ph(this, e2, (e3) => {
            var n2 = this.j._malloc(t2.length);
            this.j.HEAPU8.set(t2, n2), this.j._addProtoToInputSidePacket(n2, t2.length, e3, r3), this.j._free(n2);
          });
        });
      }
      addBoolVectorToInputSidePacket(t2, e2) {
        Ph(this, e2, (e3) => {
          var r2 = this.j._allocateBoolVector(t2.length);
          if (!r2) throw Error("Unable to allocate new bool vector on heap.");
          for (let e4 of t2) this.j._addBoolVectorEntry(r2, e4);
          this.j._addBoolVectorToInputSidePacket(r2, e3);
        });
      }
      addDoubleVectorToInputSidePacket(t2, e2) {
        Ph(this, e2, (e3) => {
          var r2 = this.j._allocateDoubleVector(t2.length);
          if (!r2) throw Error("Unable to allocate new double vector on heap.");
          for (let e4 of t2) this.j._addDoubleVectorEntry(r2, e4);
          this.j._addDoubleVectorToInputSidePacket(r2, e3);
        });
      }
      addFloatVectorToInputSidePacket(t2, e2) {
        Ph(this, e2, (e3) => {
          var r2 = this.j._allocateFloatVector(t2.length);
          if (!r2) throw Error("Unable to allocate new float vector on heap.");
          for (let e4 of t2) this.j._addFloatVectorEntry(r2, e4);
          this.j._addFloatVectorToInputSidePacket(r2, e3);
        });
      }
      addIntVectorToInputSidePacket(t2, e2) {
        Ph(this, e2, (e3) => {
          var r2 = this.j._allocateIntVector(t2.length);
          if (!r2) throw Error("Unable to allocate new int vector on heap.");
          for (let e4 of t2) this.j._addIntVectorEntry(r2, e4);
          this.j._addIntVectorToInputSidePacket(r2, e3);
        });
      }
      addUintVectorToInputSidePacket(t2, e2) {
        Ph(this, e2, (e3) => {
          var r2 = this.j._allocateUintVector(t2.length);
          if (!r2) throw Error("Unable to allocate new unsigned int vector on heap.");
          for (let e4 of t2) this.j._addUintVectorEntry(r2, e4);
          this.j._addUintVectorToInputSidePacket(r2, e3);
        });
      }
      addStringVectorToInputSidePacket(t2, e2) {
        Ph(this, e2, (e3) => {
          var r2 = this.j._allocateStringVector(t2.length);
          if (!r2) throw Error("Unable to allocate new string vector on heap.");
          for (let e4 of t2) Ph(this, e4, (t3) => {
            this.j._addStringVectorEntry(r2, t3);
          });
          this.j._addStringVectorToInputSidePacket(r2, e3);
        });
      }
      attachBoolListener(t2, e2) {
        Bh(this, t2, e2), Ph(this, t2, (t3) => {
          this.j._attachBoolListener(t3);
        });
      }
      attachBoolVectorListener(t2, e2) {
        jh(this, t2, e2), Ph(this, t2, (t3) => {
          this.j._attachBoolVectorListener(t3);
        });
      }
      attachIntListener(t2, e2) {
        Bh(this, t2, e2), Ph(this, t2, (t3) => {
          this.j._attachIntListener(t3);
        });
      }
      attachIntVectorListener(t2, e2) {
        jh(this, t2, e2), Ph(this, t2, (t3) => {
          this.j._attachIntVectorListener(t3);
        });
      }
      attachUintListener(t2, e2) {
        Bh(this, t2, e2), Ph(this, t2, (t3) => {
          this.j._attachUintListener(t3);
        });
      }
      attachUintVectorListener(t2, e2) {
        jh(this, t2, e2), Ph(this, t2, (t3) => {
          this.j._attachUintVectorListener(t3);
        });
      }
      attachDoubleListener(t2, e2) {
        Bh(this, t2, e2), Ph(this, t2, (t3) => {
          this.j._attachDoubleListener(t3);
        });
      }
      attachDoubleVectorListener(t2, e2) {
        jh(this, t2, e2), Ph(this, t2, (t3) => {
          this.j._attachDoubleVectorListener(t3);
        });
      }
      attachFloatListener(t2, e2) {
        Bh(this, t2, e2), Ph(this, t2, (t3) => {
          this.j._attachFloatListener(t3);
        });
      }
      attachFloatVectorListener(t2, e2) {
        jh(this, t2, e2), Ph(this, t2, (t3) => {
          this.j._attachFloatVectorListener(t3);
        });
      }
      attachStringListener(t2, e2) {
        Bh(this, t2, e2), Ph(this, t2, (t3) => {
          this.j._attachStringListener(t3);
        });
      }
      attachStringVectorListener(t2, e2) {
        jh(this, t2, e2), Ph(this, t2, (t3) => {
          this.j._attachStringVectorListener(t3);
        });
      }
      attachProtoListener(t2, e2, r2) {
        Bh(this, t2, e2), Ph(this, t2, (t3) => {
          this.j._attachProtoListener(t3, r2 || false);
        });
      }
      attachProtoVectorListener(t2, e2, r2) {
        jh(this, t2, e2), Ph(this, t2, (t3) => {
          this.j._attachProtoVectorListener(t3, r2 || false);
        });
      }
      attachAudioListener(t2, e2, r2) {
        this.j._attachAudioListener || console.warn('Attempting to use attachAudioListener without support for output audio. Is build dep ":gl_graph_runner_audio_out" missing?'), Bh(this, t2, (t3, r3) => {
          t3 = new Float32Array(t3.buffer, t3.byteOffset, t3.length / 4), e2(t3, r3);
        }), Ph(this, t2, (t3) => {
          this.j._attachAudioListener(t3, r2 || false);
        });
      }
      finishProcessing() {
        this.j._waitUntilIdle();
      }
      closeGraph() {
        this.j._closeGraph(), this.j.simpleListeners = void 0, this.j.emptyPacketListeners = void 0;
      }
    };
    Kh = Vh(Hh(Gh));
    Xh = class extends Kh {
    };
    ru = class {
      constructor(t2) {
        this.h = t2, this.L = [], this.J = 0, this.h.setAutoRenderToScreen(false);
      }
      l(t2, e2 = true, r2 = false, n2 = false) {
        if (e2) {
          let e3 = t2.baseOptions || {};
          if (t2.baseOptions?.modelAssetBuffer && t2.baseOptions?.modelAssetPath) throw Error("Cannot set both baseOptions.modelAssetPath and baseOptions.modelAssetBuffer");
          if (!(Tr(this.baseOptions, Wo, 1)?.h() || Tr(this.baseOptions, Wo, 1)?.l() || t2.baseOptions?.modelAssetBuffer || t2.baseOptions?.modelAssetPath)) throw Error("Either baseOptions.modelAssetPath or baseOptions.modelAssetBuffer must be set");
          !(function(t3, e4, r3 = false) {
            var n3 = Tr(t3.baseOptions, Ko, 3);
            if (!n3) {
              var i3 = n3 = new Ko(), s2 = new Qs();
              xr(i3, 4, Xo, s2);
            }
            "delegate" in e4 && ("GPU" === e4.delegate ? (e4 = n3, i3 = new Xs(), xr(e4, 2, Xo, i3)) : (e4 = n3, i3 = new Qs(), xr(e4, 4, Xo, i3))), r3 && (r3 = n3.l(), e4 = n3.h() || true === r3?.h(), r3 = new qs(), e4 ? Lr(r3, 0, 9, e4 = new zs()) : Lr(r3, 0, 8, e4 = new Ys()), xr(n3, 6, Xo, r3)), Lr(t3.baseOptions, 0, 3, n3);
          })(this, e3, n2);
          let i2 = r2 ? "model.litertlm" : "model.dat";
          if (e3.modelAssetPath) return fetch(e3.modelAssetPath.toString()).then((t3) => {
            if (t3.ok) return t3.arrayBuffer();
            throw Error(`Failed to fetch model: ${e3.modelAssetPath} (${t3.status})`);
          }).then((t3) => {
            zh(this, new Uint8Array(t3), i2), this.u(), this.M();
          });
          if (e3.modelAssetBuffer instanceof Uint8Array) r2 ? zh(this, e3.modelAssetBuffer, i2) : qh(this, e3.modelAssetBuffer);
          else if (e3.modelAssetBuffer) return (async function(t3) {
            for (var e4 = [], r3 = 0; ; ) {
              let { done: n3, value: i3 } = await t3.read();
              if (n3) break;
              e4.push(i3), r3 += i3.length;
            }
            if (0 === e4.length) return new Uint8Array(0);
            if (1 === e4.length) return e4[0];
            t3 = new Uint8Array(r3), r3 = 0;
            for (let n3 of e4) t3.set(n3, r3), r3 += n3.length;
            return t3;
          })(e3.modelAssetBuffer).then((t3) => {
            r2 ? zh(this, t3, i2) : qh(this, t3), this.u(), this.M();
          });
        }
        return this.u(), this.M(), Promise.resolve();
      }
      M() {
      }
      ka() {
        var t2;
        if (this.h.ka((e2) => {
          t2 = _o(e2);
        }), !t2) throw Error("Failed to retrieve CalculatorGraphConfig");
        return t2;
      }
      setGraph(t2, e2) {
        this.h.attachErrorListener((t3, e3) => {
          this.L.push(Error(e3));
        }), this.h.Za(), this.h.setGraph(t2, e2), this.o?.ya(), this.F = void 0, Jh(this);
      }
      finishProcessing(t2) {
        this.h.finishProcessing(), Jh(this), this.o && void 0 !== t2 && this.o.za(t2);
      }
      close() {
        this.F = void 0, this.o?.xa(), this.o?.close(), this.h.closeGraph();
      }
    };
    ru.prototype.close = ru.prototype.close;
    iu = class {
      constructor(t2, e2, r2, n2) {
        this.h = t2, this.i = e2, this.o = r2, this.l = n2;
      }
      bind() {
        this.h.bindVertexArray(this.i);
      }
      close() {
        this.h.deleteVertexArray(this.i), this.h.deleteBuffer(this.o), this.h.deleteBuffer(this.l);
      }
    };
    du = class {
      C() {
        return "\n  precision mediump float;\n  varying vec2 vTex;\n  uniform sampler2D inputTexture;\n  void main() {\n    gl_FragColor = texture2D(inputTexture, vTex);\n  }\n ";
      }
      o() {
        var t2 = this.h;
        if (this.i = nu(t2.createProgram(), "Failed to create WebGL program"), this.ea = su(this, "\n  attribute vec2 aVertex;\n  attribute vec2 aTex;\n  varying vec2 vTex;\n  void main(void) {\n    gl_Position = vec4(aVertex, 0.0, 1.0);\n    vTex = aTex;\n  }", t2.VERTEX_SHADER), this.aa = su(this, this.C(), t2.FRAGMENT_SHADER), t2.linkProgram(this.i), !t2.getProgramParameter(this.i, t2.LINK_STATUS)) throw Error(`Error during program linking: ${t2.getProgramInfoLog(this.i)}`);
        this.G = t2.getAttribLocation(this.i, "aVertex"), this.L = t2.getAttribLocation(this.i, "aTex");
      }
      J() {
      }
      l() {
      }
      close() {
        if (this.i) {
          let t2 = this.h;
          t2.deleteProgram(this.i), t2.deleteShader(this.ea), t2.deleteShader(this.aa);
        }
        this.D && this.h.deleteFramebuffer(this.D), this.F && this.F.close(), this.m && this.m.close();
      }
    };
    fu = class extends du {
      C() {
        return "\n  precision mediump float;\n  uniform sampler2D backgroundTexture;\n  uniform sampler2D maskTexture;\n  uniform sampler2D colorMappingTexture;\n  varying vec2 vTex;\n  void main() {\n    vec4 backgroundColor = texture2D(backgroundTexture, vTex);\n    float category = texture2D(maskTexture, vTex).r;\n    vec4 categoryColor = texture2D(colorMappingTexture, vec2(category, 0.0));\n    gl_FragColor = mix(backgroundColor, categoryColor, categoryColor.a);\n  }\n ";
      }
      J() {
        var t2 = this.h;
        t2.activeTexture(t2.TEXTURE1), this.v = uu(this, t2, t2.LINEAR), t2.activeTexture(t2.TEXTURE2), this.u = uu(this, t2, t2.NEAREST);
      }
      o() {
        super.o();
        var t2 = this.h;
        this.P = nu(t2.getUniformLocation(this.i, "backgroundTexture"), "Uniform location"), this.Z = nu(t2.getUniformLocation(this.i, "colorMappingTexture"), "Uniform location"), this.M = nu(t2.getUniformLocation(this.i, "maskTexture"), "Uniform location");
      }
      l() {
        super.l();
        var t2 = this.h;
        t2.uniform1i(this.M, 0), t2.uniform1i(this.P, 1), t2.uniform1i(this.Z, 2);
      }
      close() {
        this.v && this.h.deleteTexture(this.v), this.u && this.h.deleteTexture(this.u), super.close();
      }
    };
    pu = class extends du {
      C() {
        return "\n  precision mediump float;\n  uniform sampler2D maskTexture;\n  uniform sampler2D defaultTexture;\n  uniform sampler2D overlayTexture;\n  varying vec2 vTex;\n  void main() {\n    float confidence = texture2D(maskTexture, vTex).r;\n    vec4 defaultColor = texture2D(defaultTexture, vTex);\n    vec4 overlayColor = texture2D(overlayTexture, vTex);\n    // Apply the alpha from the overlay and merge in the default color\n    overlayColor = mix(defaultColor, overlayColor, overlayColor.a);\n    gl_FragColor = mix(defaultColor, overlayColor, confidence);\n  }\n ";
      }
      J() {
        var t2 = this.h;
        t2.activeTexture(t2.TEXTURE1), this.u = uu(this, t2), t2.activeTexture(t2.TEXTURE2), this.v = uu(this, t2);
      }
      o() {
        super.o();
        var t2 = this.h;
        this.M = nu(t2.getUniformLocation(this.i, "defaultTexture"), "Uniform location"), this.P = nu(t2.getUniformLocation(this.i, "overlayTexture"), "Uniform location"), this.K = nu(t2.getUniformLocation(this.i, "maskTexture"), "Uniform location");
      }
      l() {
        super.l();
        var t2 = this.h;
        t2.uniform1i(this.K, 0), t2.uniform1i(this.M, 1), t2.uniform1i(this.P, 2);
      }
      close() {
        this.u && this.h.deleteTexture(this.u), this.v && this.h.deleteTexture(this.v), super.close();
      }
    };
    bu = class {
      constructor(t2, e2, r2, n2, i2, s2, o2) {
        this.h = t2, this.o = e2, this.u = r2, this.canvas = n2, this.l = i2, this.width = s2, this.height = o2, this.u && (0 === --ku && console.error("You seem to be creating MPMask instances without invoking .close(). This leaks resources."));
      }
      Ua() {
        return !!mu(this, 0);
      }
      ua() {
        return !!mu(this, 1);
      }
      X() {
        return !!mu(this, 2);
      }
      ta() {
        return (e2 = mu(t2 = this, 0)) || (e2 = gu(t2), e2 = new Uint8Array(e2.map((t3) => Math.round(255 * t3))), t2.h.push(e2)), e2;
        var t2, e2;
      }
      sa() {
        return gu(this);
      }
      T() {
        return vu(this);
      }
      clone() {
        var t2 = [];
        for (let e2 of this.h) {
          let r2;
          if (e2 instanceof Uint8Array) r2 = new Uint8Array(e2);
          else if (e2 instanceof Float32Array) r2 = new Float32Array(e2);
          else {
            if (!(e2 instanceof WebGLTexture)) throw Error(`Type is not supported: ${e2}`);
            {
              let t3 = _u(this), e3 = Eu(this);
              t3.activeTexture(t3.TEXTURE1), r2 = uu(e3, t3, this.o ? t3.LINEAR : t3.NEAREST), t3.bindTexture(t3.TEXTURE_2D, r2);
              let n2 = yu(this);
              t3.texImage2D(t3.TEXTURE_2D, 0, n2, this.width, this.height, 0, t3.RED, t3.FLOAT, null), t3.bindTexture(t3.TEXTURE_2D, null), cu(e3, t3, r2), hu(e3, t3, false, () => {
                Au(this), t3.clearColor(0, 0, 0, 0), t3.clear(t3.COLOR_BUFFER_BIT), t3.drawArrays(t3.TRIANGLE_FAN, 0, 4), wu(this);
              }), lu(e3), wu(this);
            }
          }
          t2.push(r2);
        }
        return new bu(t2, this.o, this.X(), this.canvas, this.l, this.width, this.height);
      }
      close() {
        this.u && _u(this).deleteTexture(mu(this, 2)), ku = -1;
      }
    };
    bu.prototype.close = bu.prototype.close, bu.prototype.clone = bu.prototype.clone, bu.prototype.getAsWebGLTexture = bu.prototype.T, bu.prototype.getAsFloat32Array = bu.prototype.sa, bu.prototype.getAsUint8Array = bu.prototype.ta, bu.prototype.hasWebGLTexture = bu.prototype.X, bu.prototype.hasFloat32Array = bu.prototype.ua, bu.prototype.hasUint8Array = bu.prototype.Ua;
    ku = 250;
    Su = { color: "white", lineWidth: 4, radius: 6 };
    Mu = class {
      constructor(t2, e2) {
        "undefined" != typeof CanvasRenderingContext2D && t2 instanceof CanvasRenderingContext2D || t2 instanceof OffscreenCanvasRenderingContext2D ? (this.l = t2, this.u = e2) : this.u = t2;
      }
      Ma(t2, e2) {
        if (t2) {
          var r2 = Iu(this);
          e2 = Lu(e2), r2.save();
          var n2 = r2.canvas, i2 = 0;
          for (let s2 of t2) r2.fillStyle = xu(e2.fillColor, { index: i2, from: s2 }), r2.strokeStyle = xu(e2.color, { index: i2, from: s2 }), r2.lineWidth = xu(e2.lineWidth, { index: i2, from: s2 }), (t2 = new Path2D()).arc(s2.x * n2.width, s2.y * n2.height, xu(e2.radius, { index: i2, from: s2 }), 0, 2 * Math.PI), r2.fill(t2), r2.stroke(t2), ++i2;
          r2.restore();
        }
      }
      La(t2, e2, r2) {
        if (t2 && e2) {
          var n2 = Iu(this);
          r2 = Lu(r2), n2.save();
          var i2 = n2.canvas, s2 = 0;
          for (let o2 of e2) {
            n2.beginPath(), e2 = t2[o2.start];
            let a2 = t2[o2.end];
            e2 && a2 && (n2.strokeStyle = xu(r2.color, { index: s2, from: e2, to: a2 }), n2.lineWidth = xu(r2.lineWidth, { index: s2, from: e2, to: a2 }), n2.moveTo(e2.x * i2.width, e2.y * i2.height), n2.lineTo(a2.x * i2.width, a2.y * i2.height)), ++s2, n2.stroke();
          }
          n2.restore();
        }
      }
      Ia(t2, e2) {
        var r2 = Iu(this);
        e2 = Lu(e2), r2.save(), r2.beginPath(), r2.lineWidth = xu(e2.lineWidth, {}), r2.strokeStyle = xu(e2.color, {}), r2.fillStyle = xu(e2.fillColor, {}), r2.moveTo(t2.originX, t2.originY), r2.lineTo(t2.originX + t2.width, t2.originY), r2.lineTo(t2.originX + t2.width, t2.originY + t2.height), r2.lineTo(t2.originX, t2.originY + t2.height), r2.lineTo(t2.originX, t2.originY), r2.stroke(), r2.fill(), r2.restore();
      }
      Ja(t2, e2, r2 = [0, 0, 0, 255]) {
        this.l ? (function(t3, e3, r3, n2) {
          var i2 = Fu(t3);
          Cu(t3, e3, (e4) => {
            Nu(t3, e4, r3, n2), (e4 = Iu(t3)).drawImage(i2.canvas, 0, 0, e4.canvas.width, e4.canvas.height);
          });
        })(this, t2, r2, e2) : Nu(this, t2.T(), r2, e2);
      }
      Ka(t2, e2, r2) {
        this.l ? (function(t3, e3, r3, n2) {
          var i2 = Fu(t3);
          Cu(t3, e3, (e4) => {
            Ou(t3, e4, r3, n2), (e4 = Iu(t3)).drawImage(i2.canvas, 0, 0, e4.canvas.width, e4.canvas.height);
          });
        })(this, t2, e2, r2) : Ou(this, t2.T(), e2, r2);
      }
      close() {
        this.h?.close(), this.h = void 0, this.i?.close(), this.i = void 0, this.o?.close(), this.o = void 0;
      }
    };
    Mu.prototype.close = Mu.prototype.close, Mu.prototype.drawConfidenceMask = Mu.prototype.Ka, Mu.prototype.drawCategoryMask = Mu.prototype.Ja, Mu.prototype.drawBoundingBox = Mu.prototype.Ia, Mu.prototype.drawConnectors = Mu.prototype.La, Mu.prototype.drawLandmarks = Mu.prototype.Ma, Mu.lerp = function(t2, e2, r2, n2, i2) {
      return Ru(n2 * (1 - (t2 - e2) / (r2 - e2)) + i2 * (1 - (r2 - t2) / (r2 - e2)), n2, i2);
    }, Mu.clamp = Ru;
    Hu = class {
      constructor(t2, e2, r2, n2, i2, s2, o2) {
        this.h = t2, this.u = e2, this.o = r2, this.canvas = n2, this.l = i2, this.width = s2, this.height = o2, (this.u || this.o) && (0 === --Ku && console.error("You seem to be creating MPImage instances without invoking .close(). This leaks resources."));
      }
      Ta() {
        return !!Du(this, 0);
      }
      va() {
        return !!Du(this, 1);
      }
      X() {
        return !!Du(this, 2);
      }
      Qa() {
        return Pu(this);
      }
      Pa() {
        var t2 = Du(this, 1);
        return t2 || ($u(this), ju(this), t2 = Vu(this), Gu(this), this.h.push(t2), this.u = true), t2;
      }
      T() {
        return $u(this);
      }
      clone() {
        var t2 = [];
        for (let e2 of this.h) {
          let r2;
          if (e2 instanceof ImageData) r2 = new ImageData(e2.data, this.width, this.height);
          else if (e2 instanceof WebGLTexture) {
            let t3 = Uu(this), e3 = Bu(this);
            t3.activeTexture(t3.TEXTURE1), r2 = uu(e3, t3), t3.bindTexture(t3.TEXTURE_2D, r2), t3.texImage2D(t3.TEXTURE_2D, 0, t3.RGBA, this.width, this.height, 0, t3.RGBA, t3.UNSIGNED_BYTE, null), t3.bindTexture(t3.TEXTURE_2D, null), cu(e3, t3, r2), hu(e3, t3, false, () => {
              ju(this), t3.clearColor(0, 0, 0, 0), t3.clear(t3.COLOR_BUFFER_BIT), t3.drawArrays(t3.TRIANGLE_FAN, 0, 4), Gu(this);
            }), lu(e3), Gu(this);
          } else {
            if (!(e2 instanceof ImageBitmap)) throw Error(`Type is not supported: ${e2}`);
            $u(this), ju(this), r2 = Vu(this), Gu(this);
          }
          t2.push(r2);
        }
        return new Hu(t2, this.va(), this.X(), this.canvas, this.l, this.width, this.height);
      }
      close() {
        this.u && Du(this, 1).close(), this.o && Uu(this).deleteTexture(Du(this, 2)), Ku = -1;
      }
    };
    Hu.prototype.close = Hu.prototype.close, Hu.prototype.clone = Hu.prototype.clone, Hu.prototype.getAsWebGLTexture = Hu.prototype.T, Hu.prototype.getAsImageBitmap = Hu.prototype.Pa, Hu.prototype.getAsImageData = Hu.prototype.Qa, Hu.prototype.hasWebGLTexture = Hu.prototype.X, Hu.prototype.hasImageBitmap = Hu.prototype.va, Hu.prototype.hasImageData = Hu.prototype.Ta;
    Ku = 250;
    Yu = Hh((Wu = Vh(Gh), class extends Wu {
      get oa() {
        return this.j;
      }
      Da(t2, e2, r2) {
        Ph(this, e2, (e3) => {
          var [n2, i2] = $h(this, t2, e3);
          this.oa._addBoundTextureAsImageToStream(e3, n2, i2, r2);
        });
      }
      ha(t2, e2) {
        Bh(this, t2, e2), Ph(this, t2, (t3) => {
          this.oa._attachImageListener(t3);
        });
      }
      ia(t2, e2) {
        jh(this, t2, e2), Ph(this, t2, (t3) => {
          this.oa._attachImageVectorListener(t3);
        });
      }
    }));
    zu = class extends Yu {
    };
    ec = class extends ru {
      constructor(t2, e2, r2, n2) {
        super(t2), this.h = t2, this.Ba = e2, this.qa = r2, this.Ca = n2, this.ea = new du(), this.K = false;
      }
      l(t2, e2 = true, r2 = false, n2 = false) {
        if ("runningMode" in t2) {
          var i2 = this.K = !!t2.runningMode && "IMAGE" !== t2.runningMode;
          sr(this.baseOptions, 2, null == i2 ? i2 : Zt(i2));
        }
        if (void 0 !== t2.canvas && this.h.j.canvas !== t2.canvas) throw Error("You must create a new task to reset the canvas.");
        return super.l(t2, e2, r2, n2);
      }
      close() {
        this.ea.close(), super.close();
      }
    };
    ec.prototype.close = ec.prototype.close;
    rc = class extends ec {
      constructor(t2, e2) {
        super(new zu(t2, e2), "image_in", "norm_rect_in", false), this.m = { detections: [] }, Lr(t2 = this.i = new Zo(), 0, 1, e2 = new Yo()), Ur(this.i, 2, 0.5), Ur(this.i, 3, 0.3);
      }
      D() {
        return "FaceDetector";
      }
      get baseOptions() {
        return Tr(this.i, Yo, 1);
      }
      set baseOptions(t2) {
        Lr(this.i, 0, 1, t2);
      }
      A(t2) {
        return "minDetectionConfidence" in t2 && Ur(this.i, 2, t2.minDetectionConfidence ?? 0.5), "minSuppressionThreshold" in t2 && Ur(this.i, 3, t2.minSuppressionThreshold ?? 0.3), this.l(t2);
      }
      H(t2, e2) {
        return this.m = { detections: [] }, Zu(this, t2, e2), this.m;
      }
      I(t2, e2, r2) {
        return this.m = { detections: [] }, Qu(this, t2, r2, e2), this.m;
      }
      u() {
        var t2 = new go();
        po(t2, "image_in"), po(t2, "norm_rect_in"), mo(t2, "detections");
        var e2 = new no();
        hi(e2, ta, this.i);
        var r2 = new ho();
        jr(r2, 2, "mediapipe.tasks.vision.face_detector.FaceDetectorGraph"), oo(r2, "IMAGE:image_in"), oo(r2, "NORM_RECT:norm_rect_in"), ao(r2, "DETECTIONS:detections"), r2.A(e2), fo(t2, r2), this.h.attachProtoVectorListener("detections", (t3, e3) => {
          for (let e4 of t3) t3 = So(e4), this.m.detections.push(Za(t3));
          Zh(this, e3);
        }), this.h.attachEmptyPacketListener("detections", (t3) => {
          Zh(this, t3);
        }), t2 = t2.h(), this.setGraph(new Uint8Array(t2), true);
      }
    };
    rc.prototype.detectForVideo = rc.prototype.I, rc.prototype.detect = rc.prototype.H, rc.prototype.setOptions = rc.prototype.A, rc.createFromModelPath = async function(t2, e2) {
      return qu(rc, t2, { baseOptions: { modelAssetPath: e2 } });
    }, rc.createFromModelBuffer = function(t2, e2) {
      return qu(rc, t2, { baseOptions: { modelAssetBuffer: e2 } });
    }, rc.createFromOptions = function(t2, e2) {
      return qu(rc, t2, e2);
    };
    nc = Xu([61, 146], [146, 91], [91, 181], [181, 84], [84, 17], [17, 314], [314, 405], [405, 321], [321, 375], [375, 291], [61, 185], [185, 40], [40, 39], [39, 37], [37, 0], [0, 267], [267, 269], [269, 270], [270, 409], [409, 291], [78, 95], [95, 88], [88, 178], [178, 87], [87, 14], [14, 317], [317, 402], [402, 318], [318, 324], [324, 308], [78, 191], [191, 80], [80, 81], [81, 82], [82, 13], [13, 312], [312, 311], [311, 310], [310, 415], [415, 308]);
    ic = Xu([263, 249], [249, 390], [390, 373], [373, 374], [374, 380], [380, 381], [381, 382], [382, 362], [263, 466], [466, 388], [388, 387], [387, 386], [386, 385], [385, 384], [384, 398], [398, 362]);
    sc = Xu([276, 283], [283, 282], [282, 295], [295, 285], [300, 293], [293, 334], [334, 296], [296, 336]);
    oc = Xu([474, 475], [475, 476], [476, 477], [477, 474]);
    ac = Xu([33, 7], [7, 163], [163, 144], [144, 145], [145, 153], [153, 154], [154, 155], [155, 133], [33, 246], [246, 161], [161, 160], [160, 159], [159, 158], [158, 157], [157, 173], [173, 133]);
    hc = Xu([46, 53], [53, 52], [52, 65], [65, 55], [70, 63], [63, 105], [105, 66], [66, 107]);
    uc = Xu([469, 470], [470, 471], [471, 472], [472, 469]);
    cc = Xu([10, 338], [338, 297], [297, 332], [332, 284], [284, 251], [251, 389], [389, 356], [356, 454], [454, 323], [323, 361], [361, 288], [288, 397], [397, 365], [365, 379], [379, 378], [378, 400], [400, 377], [377, 152], [152, 148], [148, 176], [176, 149], [149, 150], [150, 136], [136, 172], [172, 58], [58, 132], [132, 93], [93, 234], [234, 127], [127, 162], [162, 21], [21, 54], [54, 103], [103, 67], [67, 109], [109, 10]);
    lc = [...nc, ...ic, ...sc, ...ac, ...hc, ...cc];
    dc = Xu([127, 34], [34, 139], [139, 127], [11, 0], [0, 37], [37, 11], [232, 231], [231, 120], [120, 232], [72, 37], [37, 39], [39, 72], [128, 121], [121, 47], [47, 128], [232, 121], [121, 128], [128, 232], [104, 69], [69, 67], [67, 104], [175, 171], [171, 148], [148, 175], [118, 50], [50, 101], [101, 118], [73, 39], [39, 40], [40, 73], [9, 151], [151, 108], [108, 9], [48, 115], [115, 131], [131, 48], [194, 204], [204, 211], [211, 194], [74, 40], [40, 185], [185, 74], [80, 42], [42, 183], [183, 80], [40, 92], [92, 186], [186, 40], [230, 229], [229, 118], [118, 230], [202, 212], [212, 214], [214, 202], [83, 18], [18, 17], [17, 83], [76, 61], [61, 146], [146, 76], [160, 29], [29, 30], [30, 160], [56, 157], [157, 173], [173, 56], [106, 204], [204, 194], [194, 106], [135, 214], [214, 192], [192, 135], [203, 165], [165, 98], [98, 203], [21, 71], [71, 68], [68, 21], [51, 45], [45, 4], [4, 51], [144, 24], [24, 23], [23, 144], [77, 146], [146, 91], [91, 77], [205, 50], [50, 187], [187, 205], [201, 200], [200, 18], [18, 201], [91, 106], [106, 182], [182, 91], [90, 91], [91, 181], [181, 90], [85, 84], [84, 17], [17, 85], [206, 203], [203, 36], [36, 206], [148, 171], [171, 140], [140, 148], [92, 40], [40, 39], [39, 92], [193, 189], [189, 244], [244, 193], [159, 158], [158, 28], [28, 159], [247, 246], [246, 161], [161, 247], [236, 3], [3, 196], [196, 236], [54, 68], [68, 104], [104, 54], [193, 168], [168, 8], [8, 193], [117, 228], [228, 31], [31, 117], [189, 193], [193, 55], [55, 189], [98, 97], [97, 99], [99, 98], [126, 47], [47, 100], [100, 126], [166, 79], [79, 218], [218, 166], [155, 154], [154, 26], [26, 155], [209, 49], [49, 131], [131, 209], [135, 136], [136, 150], [150, 135], [47, 126], [126, 217], [217, 47], [223, 52], [52, 53], [53, 223], [45, 51], [51, 134], [134, 45], [211, 170], [170, 140], [140, 211], [67, 69], [69, 108], [108, 67], [43, 106], [106, 91], [91, 43], [230, 119], [119, 120], [120, 230], [226, 130], [130, 247], [247, 226], [63, 53], [53, 52], [52, 63], [238, 20], [20, 242], [242, 238], [46, 70], [70, 156], [156, 46], [78, 62], [62, 96], [96, 78], [46, 53], [53, 63], [63, 46], [143, 34], [34, 227], [227, 143], [123, 117], [117, 111], [111, 123], [44, 125], [125, 19], [19, 44], [236, 134], [134, 51], [51, 236], [216, 206], [206, 205], [205, 216], [154, 153], [153, 22], [22, 154], [39, 37], [37, 167], [167, 39], [200, 201], [201, 208], [208, 200], [36, 142], [142, 100], [100, 36], [57, 212], [212, 202], [202, 57], [20, 60], [60, 99], [99, 20], [28, 158], [158, 157], [157, 28], [35, 226], [226, 113], [113, 35], [160, 159], [159, 27], [27, 160], [204, 202], [202, 210], [210, 204], [113, 225], [225, 46], [46, 113], [43, 202], [202, 204], [204, 43], [62, 76], [76, 77], [77, 62], [137, 123], [123, 116], [116, 137], [41, 38], [38, 72], [72, 41], [203, 129], [129, 142], [142, 203], [64, 98], [98, 240], [240, 64], [49, 102], [102, 64], [64, 49], [41, 73], [73, 74], [74, 41], [212, 216], [216, 207], [207, 212], [42, 74], [74, 184], [184, 42], [169, 170], [170, 211], [211, 169], [170, 149], [149, 176], [176, 170], [105, 66], [66, 69], [69, 105], [122, 6], [6, 168], [168, 122], [123, 147], [147, 187], [187, 123], [96, 77], [77, 90], [90, 96], [65, 55], [55, 107], [107, 65], [89, 90], [90, 180], [180, 89], [101, 100], [100, 120], [120, 101], [63, 105], [105, 104], [104, 63], [93, 137], [137, 227], [227, 93], [15, 86], [86, 85], [85, 15], [129, 102], [102, 49], [49, 129], [14, 87], [87, 86], [86, 14], [55, 8], [8, 9], [9, 55], [100, 47], [47, 121], [121, 100], [145, 23], [23, 22], [22, 145], [88, 89], [89, 179], [179, 88], [6, 122], [122, 196], [196, 6], [88, 95], [95, 96], [96, 88], [138, 172], [172, 136], [136, 138], [215, 58], [58, 172], [172, 215], [115, 48], [48, 219], [219, 115], [42, 80], [80, 81], [81, 42], [195, 3], [3, 51], [51, 195], [43, 146], [146, 61], [61, 43], [171, 175], [175, 199], [199, 171], [81, 82], [82, 38], [38, 81], [53, 46], [46, 225], [225, 53], [144, 163], [163, 110], [110, 144], [52, 65], [65, 66], [66, 52], [229, 228], [228, 117], [117, 229], [34, 127], [127, 234], [234, 34], [107, 108], [108, 69], [69, 107], [109, 108], [108, 151], [151, 109], [48, 64], [64, 235], [235, 48], [62, 78], [78, 191], [191, 62], [129, 209], [209, 126], [126, 129], [111, 35], [35, 143], [143, 111], [117, 123], [123, 50], [50, 117], [222, 65], [65, 52], [52, 222], [19, 125], [125, 141], [141, 19], [221, 55], [55, 65], [65, 221], [3, 195], [195, 197], [197, 3], [25, 7], [7, 33], [33, 25], [220, 237], [237, 44], [44, 220], [70, 71], [71, 139], [139, 70], [122, 193], [193, 245], [245, 122], [247, 130], [130, 33], [33, 247], [71, 21], [21, 162], [162, 71], [170, 169], [169, 150], [150, 170], [188, 174], [174, 196], [196, 188], [216, 186], [186, 92], [92, 216], [2, 97], [97, 167], [167, 2], [141, 125], [125, 241], [241, 141], [164, 167], [167, 37], [37, 164], [72, 38], [38, 12], [12, 72], [38, 82], [82, 13], [13, 38], [63, 68], [68, 71], [71, 63], [226, 35], [35, 111], [111, 226], [101, 50], [50, 205], [205, 101], [206, 92], [92, 165], [165, 206], [209, 198], [198, 217], [217, 209], [165, 167], [167, 97], [97, 165], [220, 115], [115, 218], [218, 220], [133, 112], [112, 243], [243, 133], [239, 238], [238, 241], [241, 239], [214, 135], [135, 169], [169, 214], [190, 173], [173, 133], [133, 190], [171, 208], [208, 32], [32, 171], [125, 44], [44, 237], [237, 125], [86, 87], [87, 178], [178, 86], [85, 86], [86, 179], [179, 85], [84, 85], [85, 180], [180, 84], [83, 84], [84, 181], [181, 83], [201, 83], [83, 182], [182, 201], [137, 93], [93, 132], [132, 137], [76, 62], [62, 183], [183, 76], [61, 76], [76, 184], [184, 61], [57, 61], [61, 185], [185, 57], [212, 57], [57, 186], [186, 212], [214, 207], [207, 187], [187, 214], [34, 143], [143, 156], [156, 34], [79, 239], [239, 237], [237, 79], [123, 137], [137, 177], [177, 123], [44, 1], [1, 4], [4, 44], [201, 194], [194, 32], [32, 201], [64, 102], [102, 129], [129, 64], [213, 215], [215, 138], [138, 213], [59, 166], [166, 219], [219, 59], [242, 99], [99, 97], [97, 242], [2, 94], [94, 141], [141, 2], [75, 59], [59, 235], [235, 75], [24, 110], [110, 228], [228, 24], [25, 130], [130, 226], [226, 25], [23, 24], [24, 229], [229, 23], [22, 23], [23, 230], [230, 22], [26, 22], [22, 231], [231, 26], [112, 26], [26, 232], [232, 112], [189, 190], [190, 243], [243, 189], [221, 56], [56, 190], [190, 221], [28, 56], [56, 221], [221, 28], [27, 28], [28, 222], [222, 27], [29, 27], [27, 223], [223, 29], [30, 29], [29, 224], [224, 30], [247, 30], [30, 225], [225, 247], [238, 79], [79, 20], [20, 238], [166, 59], [59, 75], [75, 166], [60, 75], [75, 240], [240, 60], [147, 177], [177, 215], [215, 147], [20, 79], [79, 166], [166, 20], [187, 147], [147, 213], [213, 187], [112, 233], [233, 244], [244, 112], [233, 128], [128, 245], [245, 233], [128, 114], [114, 188], [188, 128], [114, 217], [217, 174], [174, 114], [131, 115], [115, 220], [220, 131], [217, 198], [198, 236], [236, 217], [198, 131], [131, 134], [134, 198], [177, 132], [132, 58], [58, 177], [143, 35], [35, 124], [124, 143], [110, 163], [163, 7], [7, 110], [228, 110], [110, 25], [25, 228], [356, 389], [389, 368], [368, 356], [11, 302], [302, 267], [267, 11], [452, 350], [350, 349], [349, 452], [302, 303], [303, 269], [269, 302], [357, 343], [343, 277], [277, 357], [452, 453], [453, 357], [357, 452], [333, 332], [332, 297], [297, 333], [175, 152], [152, 377], [377, 175], [347, 348], [348, 330], [330, 347], [303, 304], [304, 270], [270, 303], [9, 336], [336, 337], [337, 9], [278, 279], [279, 360], [360, 278], [418, 262], [262, 431], [431, 418], [304, 408], [408, 409], [409, 304], [310, 415], [415, 407], [407, 310], [270, 409], [409, 410], [410, 270], [450, 348], [348, 347], [347, 450], [422, 430], [430, 434], [434, 422], [313, 314], [314, 17], [17, 313], [306, 307], [307, 375], [375, 306], [387, 388], [388, 260], [260, 387], [286, 414], [414, 398], [398, 286], [335, 406], [406, 418], [418, 335], [364, 367], [367, 416], [416, 364], [423, 358], [358, 327], [327, 423], [251, 284], [284, 298], [298, 251], [281, 5], [5, 4], [4, 281], [373, 374], [374, 253], [253, 373], [307, 320], [320, 321], [321, 307], [425, 427], [427, 411], [411, 425], [421, 313], [313, 18], [18, 421], [321, 405], [405, 406], [406, 321], [320, 404], [404, 405], [405, 320], [315, 16], [16, 17], [17, 315], [426, 425], [425, 266], [266, 426], [377, 400], [400, 369], [369, 377], [322, 391], [391, 269], [269, 322], [417, 465], [465, 464], [464, 417], [386, 257], [257, 258], [258, 386], [466, 260], [260, 388], [388, 466], [456, 399], [399, 419], [419, 456], [284, 332], [332, 333], [333, 284], [417, 285], [285, 8], [8, 417], [346, 340], [340, 261], [261, 346], [413, 441], [441, 285], [285, 413], [327, 460], [460, 328], [328, 327], [355, 371], [371, 329], [329, 355], [392, 439], [439, 438], [438, 392], [382, 341], [341, 256], [256, 382], [429, 420], [420, 360], [360, 429], [364, 394], [394, 379], [379, 364], [277, 343], [343, 437], [437, 277], [443, 444], [444, 283], [283, 443], [275, 440], [440, 363], [363, 275], [431, 262], [262, 369], [369, 431], [297, 338], [338, 337], [337, 297], [273, 375], [375, 321], [321, 273], [450, 451], [451, 349], [349, 450], [446, 342], [342, 467], [467, 446], [293, 334], [334, 282], [282, 293], [458, 461], [461, 462], [462, 458], [276, 353], [353, 383], [383, 276], [308, 324], [324, 325], [325, 308], [276, 300], [300, 293], [293, 276], [372, 345], [345, 447], [447, 372], [352, 345], [345, 340], [340, 352], [274, 1], [1, 19], [19, 274], [456, 248], [248, 281], [281, 456], [436, 427], [427, 425], [425, 436], [381, 256], [256, 252], [252, 381], [269, 391], [391, 393], [393, 269], [200, 199], [199, 428], [428, 200], [266, 330], [330, 329], [329, 266], [287, 273], [273, 422], [422, 287], [250, 462], [462, 328], [328, 250], [258, 286], [286, 384], [384, 258], [265, 353], [353, 342], [342, 265], [387, 259], [259, 257], [257, 387], [424, 431], [431, 430], [430, 424], [342, 353], [353, 276], [276, 342], [273, 335], [335, 424], [424, 273], [292, 325], [325, 307], [307, 292], [366, 447], [447, 345], [345, 366], [271, 303], [303, 302], [302, 271], [423, 266], [266, 371], [371, 423], [294, 455], [455, 460], [460, 294], [279, 278], [278, 294], [294, 279], [271, 272], [272, 304], [304, 271], [432, 434], [434, 427], [427, 432], [272, 407], [407, 408], [408, 272], [394, 430], [430, 431], [431, 394], [395, 369], [369, 400], [400, 395], [334, 333], [333, 299], [299, 334], [351, 417], [417, 168], [168, 351], [352, 280], [280, 411], [411, 352], [325, 319], [319, 320], [320, 325], [295, 296], [296, 336], [336, 295], [319, 403], [403, 404], [404, 319], [330, 348], [348, 349], [349, 330], [293, 298], [298, 333], [333, 293], [323, 454], [454, 447], [447, 323], [15, 16], [16, 315], [315, 15], [358, 429], [429, 279], [279, 358], [14, 15], [15, 316], [316, 14], [285, 336], [336, 9], [9, 285], [329, 349], [349, 350], [350, 329], [374, 380], [380, 252], [252, 374], [318, 402], [402, 403], [403, 318], [6, 197], [197, 419], [419, 6], [318, 319], [319, 325], [325, 318], [367, 364], [364, 365], [365, 367], [435, 367], [367, 397], [397, 435], [344, 438], [438, 439], [439, 344], [272, 271], [271, 311], [311, 272], [195, 5], [5, 281], [281, 195], [273, 287], [287, 291], [291, 273], [396, 428], [428, 199], [199, 396], [311, 271], [271, 268], [268, 311], [283, 444], [444, 445], [445, 283], [373, 254], [254, 339], [339, 373], [282, 334], [334, 296], [296, 282], [449, 347], [347, 346], [346, 449], [264, 447], [447, 454], [454, 264], [336, 296], [296, 299], [299, 336], [338, 10], [10, 151], [151, 338], [278, 439], [439, 455], [455, 278], [292, 407], [407, 415], [415, 292], [358, 371], [371, 355], [355, 358], [340, 345], [345, 372], [372, 340], [346, 347], [347, 280], [280, 346], [442, 443], [443, 282], [282, 442], [19, 94], [94, 370], [370, 19], [441, 442], [442, 295], [295, 441], [248, 419], [419, 197], [197, 248], [263, 255], [255, 359], [359, 263], [440, 275], [275, 274], [274, 440], [300, 383], [383, 368], [368, 300], [351, 412], [412, 465], [465, 351], [263, 467], [467, 466], [466, 263], [301, 368], [368, 389], [389, 301], [395, 378], [378, 379], [379, 395], [412, 351], [351, 419], [419, 412], [436, 426], [426, 322], [322, 436], [2, 164], [164, 393], [393, 2], [370, 462], [462, 461], [461, 370], [164, 0], [0, 267], [267, 164], [302, 11], [11, 12], [12, 302], [268, 12], [12, 13], [13, 268], [293, 300], [300, 301], [301, 293], [446, 261], [261, 340], [340, 446], [330, 266], [266, 425], [425, 330], [426, 423], [423, 391], [391, 426], [429, 355], [355, 437], [437, 429], [391, 327], [327, 326], [326, 391], [440, 457], [457, 438], [438, 440], [341, 382], [382, 362], [362, 341], [459, 457], [457, 461], [461, 459], [434, 430], [430, 394], [394, 434], [414, 463], [463, 362], [362, 414], [396, 369], [369, 262], [262, 396], [354, 461], [461, 457], [457, 354], [316, 403], [403, 402], [402, 316], [315, 404], [404, 403], [403, 315], [314, 405], [405, 404], [404, 314], [313, 406], [406, 405], [405, 313], [421, 418], [418, 406], [406, 421], [366, 401], [401, 361], [361, 366], [306, 408], [408, 407], [407, 306], [291, 409], [409, 408], [408, 291], [287, 410], [410, 409], [409, 287], [432, 436], [436, 410], [410, 432], [434, 416], [416, 411], [411, 434], [264, 368], [368, 383], [383, 264], [309, 438], [438, 457], [457, 309], [352, 376], [376, 401], [401, 352], [274, 275], [275, 4], [4, 274], [421, 428], [428, 262], [262, 421], [294, 327], [327, 358], [358, 294], [433, 416], [416, 367], [367, 433], [289, 455], [455, 439], [439, 289], [462, 370], [370, 326], [326, 462], [2, 326], [326, 370], [370, 2], [305, 460], [460, 455], [455, 305], [254, 449], [449, 448], [448, 254], [255, 261], [261, 446], [446, 255], [253, 450], [450, 449], [449, 253], [252, 451], [451, 450], [450, 252], [256, 452], [452, 451], [451, 256], [341, 453], [453, 452], [452, 341], [413, 464], [464, 463], [463, 413], [441, 413], [413, 414], [414, 441], [258, 442], [442, 441], [441, 258], [257, 443], [443, 442], [442, 257], [259, 444], [444, 443], [443, 259], [260, 445], [445, 444], [444, 260], [467, 342], [342, 445], [445, 467], [459, 458], [458, 250], [250, 459], [289, 392], [392, 290], [290, 289], [290, 328], [328, 460], [460, 290], [376, 433], [433, 435], [435, 376], [250, 290], [290, 392], [392, 250], [411, 416], [416, 433], [433, 411], [341, 463], [463, 464], [464, 341], [453, 464], [464, 465], [465, 453], [357, 465], [465, 412], [412, 357], [343, 412], [412, 399], [399, 343], [360, 363], [363, 440], [440, 360], [437, 399], [399, 456], [456, 437], [420, 456], [456, 363], [363, 420], [401, 435], [435, 288], [288, 401], [372, 383], [383, 353], [353, 372], [339, 255], [255, 249], [249, 339], [448, 261], [261, 255], [255, 448], [133, 243], [243, 190], [190, 133], [133, 155], [155, 112], [112, 133], [33, 246], [246, 247], [247, 33], [33, 130], [130, 25], [25, 33], [398, 384], [384, 286], [286, 398], [362, 398], [398, 414], [414, 362], [362, 463], [463, 341], [341, 362], [263, 359], [359, 467], [467, 263], [263, 249], [249, 255], [255, 263], [466, 467], [467, 260], [260, 466], [75, 60], [60, 166], [166, 75], [238, 239], [239, 79], [79, 238], [162, 127], [127, 139], [139, 162], [72, 11], [11, 37], [37, 72], [121, 232], [232, 120], [120, 121], [73, 72], [72, 39], [39, 73], [114, 128], [128, 47], [47, 114], [233, 232], [232, 128], [128, 233], [103, 104], [104, 67], [67, 103], [152, 175], [175, 148], [148, 152], [119, 118], [118, 101], [101, 119], [74, 73], [73, 40], [40, 74], [107, 9], [9, 108], [108, 107], [49, 48], [48, 131], [131, 49], [32, 194], [194, 211], [211, 32], [184, 74], [74, 185], [185, 184], [191, 80], [80, 183], [183, 191], [185, 40], [40, 186], [186, 185], [119, 230], [230, 118], [118, 119], [210, 202], [202, 214], [214, 210], [84, 83], [83, 17], [17, 84], [77, 76], [76, 146], [146, 77], [161, 160], [160, 30], [30, 161], [190, 56], [56, 173], [173, 190], [182, 106], [106, 194], [194, 182], [138, 135], [135, 192], [192, 138], [129, 203], [203, 98], [98, 129], [54, 21], [21, 68], [68, 54], [5, 51], [51, 4], [4, 5], [145, 144], [144, 23], [23, 145], [90, 77], [77, 91], [91, 90], [207, 205], [205, 187], [187, 207], [83, 201], [201, 18], [18, 83], [181, 91], [91, 182], [182, 181], [180, 90], [90, 181], [181, 180], [16, 85], [85, 17], [17, 16], [205, 206], [206, 36], [36, 205], [176, 148], [148, 140], [140, 176], [165, 92], [92, 39], [39, 165], [245, 193], [193, 244], [244, 245], [27, 159], [159, 28], [28, 27], [30, 247], [247, 161], [161, 30], [174, 236], [236, 196], [196, 174], [103, 54], [54, 104], [104, 103], [55, 193], [193, 8], [8, 55], [111, 117], [117, 31], [31, 111], [221, 189], [189, 55], [55, 221], [240, 98], [98, 99], [99, 240], [142, 126], [126, 100], [100, 142], [219, 166], [166, 218], [218, 219], [112, 155], [155, 26], [26, 112], [198, 209], [209, 131], [131, 198], [169, 135], [135, 150], [150, 169], [114, 47], [47, 217], [217, 114], [224, 223], [223, 53], [53, 224], [220, 45], [45, 134], [134, 220], [32, 211], [211, 140], [140, 32], [109, 67], [67, 108], [108, 109], [146, 43], [43, 91], [91, 146], [231, 230], [230, 120], [120, 231], [113, 226], [226, 247], [247, 113], [105, 63], [63, 52], [52, 105], [241, 238], [238, 242], [242, 241], [124, 46], [46, 156], [156, 124], [95, 78], [78, 96], [96, 95], [70, 46], [46, 63], [63, 70], [116, 143], [143, 227], [227, 116], [116, 123], [123, 111], [111, 116], [1, 44], [44, 19], [19, 1], [3, 236], [236, 51], [51, 3], [207, 216], [216, 205], [205, 207], [26, 154], [154, 22], [22, 26], [165, 39], [39, 167], [167, 165], [199, 200], [200, 208], [208, 199], [101, 36], [36, 100], [100, 101], [43, 57], [57, 202], [202, 43], [242, 20], [20, 99], [99, 242], [56, 28], [28, 157], [157, 56], [124, 35], [35, 113], [113, 124], [29, 160], [160, 27], [27, 29], [211, 204], [204, 210], [210, 211], [124, 113], [113, 46], [46, 124], [106, 43], [43, 204], [204, 106], [96, 62], [62, 77], [77, 96], [227, 137], [137, 116], [116, 227], [73, 41], [41, 72], [72, 73], [36, 203], [203, 142], [142, 36], [235, 64], [64, 240], [240, 235], [48, 49], [49, 64], [64, 48], [42, 41], [41, 74], [74, 42], [214, 212], [212, 207], [207, 214], [183, 42], [42, 184], [184, 183], [210, 169], [169, 211], [211, 210], [140, 170], [170, 176], [176, 140], [104, 105], [105, 69], [69, 104], [193, 122], [122, 168], [168, 193], [50, 123], [123, 187], [187, 50], [89, 96], [96, 90], [90, 89], [66, 65], [65, 107], [107, 66], [179, 89], [89, 180], [180, 179], [119, 101], [101, 120], [120, 119], [68, 63], [63, 104], [104, 68], [234, 93], [93, 227], [227, 234], [16, 15], [15, 85], [85, 16], [209, 129], [129, 49], [49, 209], [15, 14], [14, 86], [86, 15], [107, 55], [55, 9], [9, 107], [120, 100], [100, 121], [121, 120], [153, 145], [145, 22], [22, 153], [178, 88], [88, 179], [179, 178], [197, 6], [6, 196], [196, 197], [89, 88], [88, 96], [96, 89], [135, 138], [138, 136], [136, 135], [138, 215], [215, 172], [172, 138], [218, 115], [115, 219], [219, 218], [41, 42], [42, 81], [81, 41], [5, 195], [195, 51], [51, 5], [57, 43], [43, 61], [61, 57], [208, 171], [171, 199], [199, 208], [41, 81], [81, 38], [38, 41], [224, 53], [53, 225], [225, 224], [24, 144], [144, 110], [110, 24], [105, 52], [52, 66], [66, 105], [118, 229], [229, 117], [117, 118], [227, 34], [34, 234], [234, 227], [66, 107], [107, 69], [69, 66], [10, 109], [109, 151], [151, 10], [219, 48], [48, 235], [235, 219], [183, 62], [62, 191], [191, 183], [142, 129], [129, 126], [126, 142], [116, 111], [111, 143], [143, 116], [118, 117], [117, 50], [50, 118], [223, 222], [222, 52], [52, 223], [94, 19], [19, 141], [141, 94], [222, 221], [221, 65], [65, 222], [196, 3], [3, 197], [197, 196], [45, 220], [220, 44], [44, 45], [156, 70], [70, 139], [139, 156], [188, 122], [122, 245], [245, 188], [139, 71], [71, 162], [162, 139], [149, 170], [170, 150], [150, 149], [122, 188], [188, 196], [196, 122], [206, 216], [216, 92], [92, 206], [164, 2], [2, 167], [167, 164], [242, 141], [141, 241], [241, 242], [0, 164], [164, 37], [37, 0], [11, 72], [72, 12], [12, 11], [12, 38], [38, 13], [13, 12], [70, 63], [63, 71], [71, 70], [31, 226], [226, 111], [111, 31], [36, 101], [101, 205], [205, 36], [203, 206], [206, 165], [165, 203], [126, 209], [209, 217], [217, 126], [98, 165], [165, 97], [97, 98], [237, 220], [220, 218], [218, 237], [237, 239], [239, 241], [241, 237], [210, 214], [214, 169], [169, 210], [140, 171], [171, 32], [32, 140], [241, 125], [125, 237], [237, 241], [179, 86], [86, 178], [178, 179], [180, 85], [85, 179], [179, 180], [181, 84], [84, 180], [180, 181], [182, 83], [83, 181], [181, 182], [194, 201], [201, 182], [182, 194], [177, 137], [137, 132], [132, 177], [184, 76], [76, 183], [183, 184], [185, 61], [61, 184], [184, 185], [186, 57], [57, 185], [185, 186], [216, 212], [212, 186], [186, 216], [192, 214], [214, 187], [187, 192], [139, 34], [34, 156], [156, 139], [218, 79], [79, 237], [237, 218], [147, 123], [123, 177], [177, 147], [45, 44], [44, 4], [4, 45], [208, 201], [201, 32], [32, 208], [98, 64], [64, 129], [129, 98], [192, 213], [213, 138], [138, 192], [235, 59], [59, 219], [219, 235], [141, 242], [242, 97], [97, 141], [97, 2], [2, 141], [141, 97], [240, 75], [75, 235], [235, 240], [229, 24], [24, 228], [228, 229], [31, 25], [25, 226], [226, 31], [230, 23], [23, 229], [229, 230], [231, 22], [22, 230], [230, 231], [232, 26], [26, 231], [231, 232], [233, 112], [112, 232], [232, 233], [244, 189], [189, 243], [243, 244], [189, 221], [221, 190], [190, 189], [222, 28], [28, 221], [221, 222], [223, 27], [27, 222], [222, 223], [224, 29], [29, 223], [223, 224], [225, 30], [30, 224], [224, 225], [113, 247], [247, 225], [225, 113], [99, 60], [60, 240], [240, 99], [213, 147], [147, 215], [215, 213], [60, 20], [20, 166], [166, 60], [192, 187], [187, 213], [213, 192], [243, 112], [112, 244], [244, 243], [244, 233], [233, 245], [245, 244], [245, 128], [128, 188], [188, 245], [188, 114], [114, 174], [174, 188], [134, 131], [131, 220], [220, 134], [174, 217], [217, 236], [236, 174], [236, 198], [198, 134], [134, 236], [215, 177], [177, 58], [58, 215], [156, 143], [143, 124], [124, 156], [25, 110], [110, 7], [7, 25], [31, 228], [228, 25], [25, 31], [264, 356], [356, 368], [368, 264], [0, 11], [11, 267], [267, 0], [451, 452], [452, 349], [349, 451], [267, 302], [302, 269], [269, 267], [350, 357], [357, 277], [277, 350], [350, 452], [452, 357], [357, 350], [299, 333], [333, 297], [297, 299], [396, 175], [175, 377], [377, 396], [280, 347], [347, 330], [330, 280], [269, 303], [303, 270], [270, 269], [151, 9], [9, 337], [337, 151], [344, 278], [278, 360], [360, 344], [424, 418], [418, 431], [431, 424], [270, 304], [304, 409], [409, 270], [272, 310], [310, 407], [407, 272], [322, 270], [270, 410], [410, 322], [449, 450], [450, 347], [347, 449], [432, 422], [422, 434], [434, 432], [18, 313], [313, 17], [17, 18], [291, 306], [306, 375], [375, 291], [259, 387], [387, 260], [260, 259], [424, 335], [335, 418], [418, 424], [434, 364], [364, 416], [416, 434], [391, 423], [423, 327], [327, 391], [301, 251], [251, 298], [298, 301], [275, 281], [281, 4], [4, 275], [254, 373], [373, 253], [253, 254], [375, 307], [307, 321], [321, 375], [280, 425], [425, 411], [411, 280], [200, 421], [421, 18], [18, 200], [335, 321], [321, 406], [406, 335], [321, 320], [320, 405], [405, 321], [314, 315], [315, 17], [17, 314], [423, 426], [426, 266], [266, 423], [396, 377], [377, 369], [369, 396], [270, 322], [322, 269], [269, 270], [413, 417], [417, 464], [464, 413], [385, 386], [386, 258], [258, 385], [248, 456], [456, 419], [419, 248], [298, 284], [284, 333], [333, 298], [168, 417], [417, 8], [8, 168], [448, 346], [346, 261], [261, 448], [417, 413], [413, 285], [285, 417], [326, 327], [327, 328], [328, 326], [277, 355], [355, 329], [329, 277], [309, 392], [392, 438], [438, 309], [381, 382], [382, 256], [256, 381], [279, 429], [429, 360], [360, 279], [365, 364], [364, 379], [379, 365], [355, 277], [277, 437], [437, 355], [282, 443], [443, 283], [283, 282], [281, 275], [275, 363], [363, 281], [395, 431], [431, 369], [369, 395], [299, 297], [297, 337], [337, 299], [335, 273], [273, 321], [321, 335], [348, 450], [450, 349], [349, 348], [359, 446], [446, 467], [467, 359], [283, 293], [293, 282], [282, 283], [250, 458], [458, 462], [462, 250], [300, 276], [276, 383], [383, 300], [292, 308], [308, 325], [325, 292], [283, 276], [276, 293], [293, 283], [264, 372], [372, 447], [447, 264], [346, 352], [352, 340], [340, 346], [354, 274], [274, 19], [19, 354], [363, 456], [456, 281], [281, 363], [426, 436], [436, 425], [425, 426], [380, 381], [381, 252], [252, 380], [267, 269], [269, 393], [393, 267], [421, 200], [200, 428], [428, 421], [371, 266], [266, 329], [329, 371], [432, 287], [287, 422], [422, 432], [290, 250], [250, 328], [328, 290], [385, 258], [258, 384], [384, 385], [446, 265], [265, 342], [342, 446], [386, 387], [387, 257], [257, 386], [422, 424], [424, 430], [430, 422], [445, 342], [342, 276], [276, 445], [422, 273], [273, 424], [424, 422], [306, 292], [292, 307], [307, 306], [352, 366], [366, 345], [345, 352], [268, 271], [271, 302], [302, 268], [358, 423], [423, 371], [371, 358], [327, 294], [294, 460], [460, 327], [331, 279], [279, 294], [294, 331], [303, 271], [271, 304], [304, 303], [436, 432], [432, 427], [427, 436], [304, 272], [272, 408], [408, 304], [395, 394], [394, 431], [431, 395], [378, 395], [395, 400], [400, 378], [296, 334], [334, 299], [299, 296], [6, 351], [351, 168], [168, 6], [376, 352], [352, 411], [411, 376], [307, 325], [325, 320], [320, 307], [285, 295], [295, 336], [336, 285], [320, 319], [319, 404], [404, 320], [329, 330], [330, 349], [349, 329], [334, 293], [293, 333], [333, 334], [366, 323], [323, 447], [447, 366], [316, 15], [15, 315], [315, 316], [331, 358], [358, 279], [279, 331], [317, 14], [14, 316], [316, 317], [8, 285], [285, 9], [9, 8], [277, 329], [329, 350], [350, 277], [253, 374], [374, 252], [252, 253], [319, 318], [318, 403], [403, 319], [351, 6], [6, 419], [419, 351], [324, 318], [318, 325], [325, 324], [397, 367], [367, 365], [365, 397], [288, 435], [435, 397], [397, 288], [278, 344], [344, 439], [439, 278], [310, 272], [272, 311], [311, 310], [248, 195], [195, 281], [281, 248], [375, 273], [273, 291], [291, 375], [175, 396], [396, 199], [199, 175], [312, 311], [311, 268], [268, 312], [276, 283], [283, 445], [445, 276], [390, 373], [373, 339], [339, 390], [295, 282], [282, 296], [296, 295], [448, 449], [449, 346], [346, 448], [356, 264], [264, 454], [454, 356], [337, 336], [336, 299], [299, 337], [337, 338], [338, 151], [151, 337], [294, 278], [278, 455], [455, 294], [308, 292], [292, 415], [415, 308], [429, 358], [358, 355], [355, 429], [265, 340], [340, 372], [372, 265], [352, 346], [346, 280], [280, 352], [295, 442], [442, 282], [282, 295], [354, 19], [19, 370], [370, 354], [285, 441], [441, 295], [295, 285], [195, 248], [248, 197], [197, 195], [457, 440], [440, 274], [274, 457], [301, 300], [300, 368], [368, 301], [417, 351], [351, 465], [465, 417], [251, 301], [301, 389], [389, 251], [394, 395], [395, 379], [379, 394], [399, 412], [412, 419], [419, 399], [410, 436], [436, 322], [322, 410], [326, 2], [2, 393], [393, 326], [354, 370], [370, 461], [461, 354], [393, 164], [164, 267], [267, 393], [268, 302], [302, 12], [12, 268], [312, 268], [268, 13], [13, 312], [298, 293], [293, 301], [301, 298], [265, 446], [446, 340], [340, 265], [280, 330], [330, 425], [425, 280], [322, 426], [426, 391], [391, 322], [420, 429], [429, 437], [437, 420], [393, 391], [391, 326], [326, 393], [344, 440], [440, 438], [438, 344], [458, 459], [459, 461], [461, 458], [364, 434], [434, 394], [394, 364], [428, 396], [396, 262], [262, 428], [274, 354], [354, 457], [457, 274], [317, 316], [316, 402], [402, 317], [316, 315], [315, 403], [403, 316], [315, 314], [314, 404], [404, 315], [314, 313], [313, 405], [405, 314], [313, 421], [421, 406], [406, 313], [323, 366], [366, 361], [361, 323], [292, 306], [306, 407], [407, 292], [306, 291], [291, 408], [408, 306], [291, 287], [287, 409], [409, 291], [287, 432], [432, 410], [410, 287], [427, 434], [434, 411], [411, 427], [372, 264], [264, 383], [383, 372], [459, 309], [309, 457], [457, 459], [366, 352], [352, 401], [401, 366], [1, 274], [274, 4], [4, 1], [418, 421], [421, 262], [262, 418], [331, 294], [294, 358], [358, 331], [435, 433], [433, 367], [367, 435], [392, 289], [289, 439], [439, 392], [328, 462], [462, 326], [326, 328], [94, 2], [2, 370], [370, 94], [289, 305], [305, 455], [455, 289], [339, 254], [254, 448], [448, 339], [359, 255], [255, 446], [446, 359], [254, 253], [253, 449], [449, 254], [253, 252], [252, 450], [450, 253], [252, 256], [256, 451], [451, 252], [256, 341], [341, 452], [452, 256], [414, 413], [413, 463], [463, 414], [286, 441], [441, 414], [414, 286], [286, 258], [258, 441], [441, 286], [258, 257], [257, 442], [442, 258], [257, 259], [259, 443], [443, 257], [259, 260], [260, 444], [444, 259], [260, 467], [467, 445], [445, 260], [309, 459], [459, 250], [250, 309], [305, 289], [289, 290], [290, 305], [305, 290], [290, 460], [460, 305], [401, 376], [376, 435], [435, 401], [309, 250], [250, 392], [392, 309], [376, 411], [411, 433], [433, 376], [453, 341], [341, 464], [464, 453], [357, 453], [453, 465], [465, 357], [343, 357], [357, 412], [412, 343], [437, 343], [343, 399], [399, 437], [344, 360], [360, 440], [440, 344], [420, 437], [437, 456], [456, 420], [360, 420], [420, 363], [363, 360], [361, 401], [401, 288], [288, 361], [265, 372], [372, 353], [353, 265], [390, 339], [339, 249], [249, 390], [339, 448], [448, 255], [255, 339]);
    pc = class extends ec {
      constructor(t2, e2) {
        super(new zu(t2, e2), "image_in", "norm_rect", false), this.m = { faceLandmarks: [], faceBlendshapes: [], facialTransformationMatrixes: [] }, this.outputFacialTransformationMatrixes = this.outputFaceBlendshapes = false, Lr(t2 = this.i = new na(), 0, 1, e2 = new Yo()), this.C = new ra(), Lr(this.i, 0, 3, this.C), this.v = new Zo(), Lr(this.i, 0, 2, this.v), Dr(this.v, 4, 1), Ur(this.v, 2, 0.5), Ur(this.C, 2, 0.5), Ur(this.i, 4, 0.5);
      }
      D() {
        return "FaceLandmarker";
      }
      get baseOptions() {
        return Tr(this.i, Yo, 1);
      }
      set baseOptions(t2) {
        Lr(this.i, 0, 1, t2);
      }
      A(t2) {
        return "numFaces" in t2 && Dr(this.v, 4, t2.numFaces ?? 1), "minFaceDetectionConfidence" in t2 && Ur(this.v, 2, t2.minFaceDetectionConfidence ?? 0.5), "minTrackingConfidence" in t2 && Ur(this.i, 4, t2.minTrackingConfidence ?? 0.5), "minFacePresenceConfidence" in t2 && Ur(this.C, 2, t2.minFacePresenceConfidence ?? 0.5), "outputFaceBlendshapes" in t2 && (this.outputFaceBlendshapes = !!t2.outputFaceBlendshapes), "outputFacialTransformationMatrixes" in t2 && (this.outputFacialTransformationMatrixes = !!t2.outputFacialTransformationMatrixes), this.l(t2);
      }
      H(t2, e2) {
        return fc(this), Zu(this, t2, e2), this.m;
      }
      I(t2, e2, r2) {
        return fc(this), Qu(this, t2, r2, e2), this.m;
      }
      u() {
        var t2 = new go();
        po(t2, "image_in"), po(t2, "norm_rect"), mo(t2, "face_landmarks");
        var e2 = new no();
        hi(e2, sa, this.i);
        var r2 = new ho();
        jr(r2, 2, "mediapipe.tasks.vision.face_landmarker.FaceLandmarkerGraph"), oo(r2, "IMAGE:image_in"), oo(r2, "NORM_RECT:norm_rect"), ao(r2, "NORM_LANDMARKS:face_landmarks"), r2.A(e2), fo(t2, r2), this.h.attachProtoVectorListener("face_landmarks", (t3, e3) => {
          for (let e4 of t3) t3 = Io(e4), this.m.faceLandmarks.push(Qa(t3));
          Zh(this, e3);
        }), this.h.attachEmptyPacketListener("face_landmarks", (t3) => {
          Zh(this, t3);
        }), this.outputFaceBlendshapes && (mo(t2, "blendshapes"), ao(r2, "BLENDSHAPES:blendshapes"), this.h.attachProtoVectorListener("blendshapes", (t3, e3) => {
          if (this.outputFaceBlendshapes) for (let e4 of t3) t3 = wo(e4), this.m.faceBlendshapes.push(qa(t3.h() ?? []));
          Zh(this, e3);
        }), this.h.attachEmptyPacketListener("blendshapes", (t3) => {
          Zh(this, t3);
        })), this.outputFacialTransformationMatrixes && (mo(t2, "face_geometry"), ao(r2, "FACE_GEOMETRY:face_geometry"), this.h.attachProtoVectorListener("face_geometry", (t3, e3) => {
          if (this.outputFacialTransformationMatrixes) for (let e4 of t3) (t3 = Tr(t3 = ea(e4), Fo, 2)) && this.m.facialTransformationMatrixes.push({ rows: Cr(t3, 1) ?? 0 ?? 0, columns: Cr(t3, 2) ?? 0 ?? 0, data: ur(t3, 3, Jt).slice() ?? [] });
          Zh(this, e3);
        }), this.h.attachEmptyPacketListener("face_geometry", (t3) => {
          Zh(this, t3);
        })), t2 = t2.h(), this.setGraph(new Uint8Array(t2), true);
      }
    };
    pc.prototype.detectForVideo = pc.prototype.I, pc.prototype.detect = pc.prototype.H, pc.prototype.setOptions = pc.prototype.A, pc.createFromModelPath = function(t2, e2) {
      return qu(pc, t2, { baseOptions: { modelAssetPath: e2 } });
    }, pc.createFromModelBuffer = function(t2, e2) {
      return qu(pc, t2, { baseOptions: { modelAssetBuffer: e2 } });
    }, pc.createFromOptions = function(t2, e2) {
      return qu(pc, t2, e2);
    }, pc.FACE_LANDMARKS_LIPS = nc, e("module$exports$google3$third_party$mediapipe$tasks$web$vision$face_landmarker$face_landmarker.FaceLandmarker.FACE_LANDMARKS_LIPS", pc.FACE_LANDMARKS_LIPS), pc.FACE_LANDMARKS_LEFT_EYE = ic, e("module$exports$google3$third_party$mediapipe$tasks$web$vision$face_landmarker$face_landmarker.FaceLandmarker.FACE_LANDMARKS_LEFT_EYE", pc.FACE_LANDMARKS_LEFT_EYE), pc.FACE_LANDMARKS_LEFT_EYEBROW = sc, e("module$exports$google3$third_party$mediapipe$tasks$web$vision$face_landmarker$face_landmarker.FaceLandmarker.FACE_LANDMARKS_LEFT_EYEBROW", pc.FACE_LANDMARKS_LEFT_EYEBROW), pc.FACE_LANDMARKS_LEFT_IRIS = oc, e("module$exports$google3$third_party$mediapipe$tasks$web$vision$face_landmarker$face_landmarker.FaceLandmarker.FACE_LANDMARKS_LEFT_IRIS", pc.FACE_LANDMARKS_LEFT_IRIS), pc.FACE_LANDMARKS_RIGHT_EYE = ac, e("module$exports$google3$third_party$mediapipe$tasks$web$vision$face_landmarker$face_landmarker.FaceLandmarker.FACE_LANDMARKS_RIGHT_EYE", pc.FACE_LANDMARKS_RIGHT_EYE), pc.FACE_LANDMARKS_RIGHT_EYEBROW = hc, e("module$exports$google3$third_party$mediapipe$tasks$web$vision$face_landmarker$face_landmarker.FaceLandmarker.FACE_LANDMARKS_RIGHT_EYEBROW", pc.FACE_LANDMARKS_RIGHT_EYEBROW), pc.FACE_LANDMARKS_RIGHT_IRIS = uc, e("module$exports$google3$third_party$mediapipe$tasks$web$vision$face_landmarker$face_landmarker.FaceLandmarker.FACE_LANDMARKS_RIGHT_IRIS", pc.FACE_LANDMARKS_RIGHT_IRIS), pc.FACE_LANDMARKS_FACE_OVAL = cc, e("module$exports$google3$third_party$mediapipe$tasks$web$vision$face_landmarker$face_landmarker.FaceLandmarker.FACE_LANDMARKS_FACE_OVAL", pc.FACE_LANDMARKS_FACE_OVAL), pc.FACE_LANDMARKS_CONTOURS = lc, e("module$exports$google3$third_party$mediapipe$tasks$web$vision$face_landmarker$face_landmarker.FaceLandmarker.FACE_LANDMARKS_CONTOURS", pc.FACE_LANDMARKS_CONTOURS), pc.FACE_LANDMARKS_TESSELATION = dc, e("module$exports$google3$third_party$mediapipe$tasks$web$vision$face_landmarker$face_landmarker.FaceLandmarker.FACE_LANDMARKS_TESSELATION", pc.FACE_LANDMARKS_TESSELATION);
    mc = Xu([0, 1], [1, 2], [2, 3], [3, 4], [0, 5], [5, 6], [6, 7], [7, 8], [5, 9], [9, 10], [10, 11], [11, 12], [9, 13], [13, 14], [14, 15], [15, 16], [13, 17], [0, 17], [17, 18], [18, 19], [19, 20]);
    yc = class extends ec {
      constructor(t2, e2) {
        super(new zu(t2, e2), "image_in", "norm_rect", false), this.gestures = [], this.landmarks = [], this.worldLandmarks = [], this.handedness = [], Lr(t2 = this.m = new da(), 0, 1, e2 = new Yo()), this.v = new la(), Lr(this.m, 0, 2, this.v), this.G = new ca(), Lr(this.v, 0, 3, this.G), this.C = new ua(), Lr(this.v, 0, 2, this.C), this.i = new ha(), Lr(this.m, 0, 3, this.i), Ur(this.C, 2, 0.5), Ur(this.v, 4, 0.5), Ur(this.G, 2, 0.5);
      }
      D() {
        return "GestureRecognizer";
      }
      get baseOptions() {
        return Tr(this.m, Yo, 1);
      }
      set baseOptions(t2) {
        Lr(this.m, 0, 1, t2);
      }
      A(t2) {
        if (Dr(this.C, 3, t2.numHands ?? 1), "minHandDetectionConfidence" in t2 && Ur(this.C, 2, t2.minHandDetectionConfidence ?? 0.5), "minTrackingConfidence" in t2 && Ur(this.v, 4, t2.minTrackingConfidence ?? 0.5), "minHandPresenceConfidence" in t2 && Ur(this.G, 2, t2.minHandPresenceConfidence ?? 0.5), t2.cannedGesturesClassifierOptions) {
          var e2 = new oa(), r2 = e2, n2 = Ya(t2.cannedGesturesClassifierOptions, Tr(this.i, oa, 3)?.l());
          Lr(r2, 0, 2, n2), Lr(this.i, 0, 3, e2);
        } else void 0 === t2.cannedGesturesClassifierOptions && Tr(this.i, oa, 3)?.h();
        return t2.customGesturesClassifierOptions ? (Lr(r2 = e2 = new oa(), 0, 2, n2 = Ya(t2.customGesturesClassifierOptions, Tr(this.i, oa, 4)?.l())), Lr(this.i, 0, 4, e2)) : void 0 === t2.customGesturesClassifierOptions && Tr(this.i, oa, 4)?.h(), this.l(t2);
      }
      Xa(t2, e2) {
        return gc(this), Zu(this, t2, e2), vc(this);
      }
      Ya(t2, e2, r2) {
        return gc(this), Qu(this, t2, r2, e2), vc(this);
      }
      u() {
        var t2 = new go();
        po(t2, "image_in"), po(t2, "norm_rect"), mo(t2, "hand_gestures"), mo(t2, "hand_landmarks"), mo(t2, "world_hand_landmarks"), mo(t2, "handedness");
        var e2 = new no();
        hi(e2, va, this.m);
        var r2 = new ho();
        jr(r2, 2, "mediapipe.tasks.vision.gesture_recognizer.GestureRecognizerGraph"), oo(r2, "IMAGE:image_in"), oo(r2, "NORM_RECT:norm_rect"), ao(r2, "HAND_GESTURES:hand_gestures"), ao(r2, "LANDMARKS:hand_landmarks"), ao(r2, "WORLD_LANDMARKS:world_hand_landmarks"), ao(r2, "HANDEDNESS:handedness"), r2.A(e2), fo(t2, r2), this.h.attachProtoVectorListener("hand_landmarks", (t3, e3) => {
          for (let e4 of t3) t3 = Io(e4), this.landmarks.push(Qa(t3));
          Zh(this, e3);
        }), this.h.attachEmptyPacketListener("hand_landmarks", (t3) => {
          Zh(this, t3);
        }), this.h.attachProtoVectorListener("world_hand_landmarks", (t3, e3) => {
          for (let e4 of t3) t3 = xo(e4), this.worldLandmarks.push(th(t3));
          Zh(this, e3);
        }), this.h.attachEmptyPacketListener("world_hand_landmarks", (t3) => {
          Zh(this, t3);
        }), this.h.attachProtoVectorListener("hand_gestures", (t3, e3) => {
          this.gestures.push(..._c(t3, false)), Zh(this, e3);
        }), this.h.attachEmptyPacketListener("hand_gestures", (t3) => {
          Zh(this, t3);
        }), this.h.attachProtoVectorListener("handedness", (t3, e3) => {
          this.handedness.push(..._c(t3)), Zh(this, e3);
        }), this.h.attachEmptyPacketListener("handedness", (t3) => {
          Zh(this, t3);
        }), t2 = t2.h(), this.setGraph(new Uint8Array(t2), true);
      }
    };
    yc.prototype.recognizeForVideo = yc.prototype.Ya, yc.prototype.recognize = yc.prototype.Xa, yc.prototype.setOptions = yc.prototype.A, yc.createFromModelPath = function(t2, e2) {
      return qu(yc, t2, { baseOptions: { modelAssetPath: e2 } });
    }, yc.createFromModelBuffer = function(t2, e2) {
      return qu(yc, t2, { baseOptions: { modelAssetBuffer: e2 } });
    }, yc.createFromOptions = function(t2, e2) {
      return qu(yc, t2, e2);
    }, yc.HAND_CONNECTIONS = mc, e("module$exports$google3$third_party$mediapipe$tasks$web$vision$gesture_recognizer$gesture_recognizer.GestureRecognizer.HAND_CONNECTIONS", yc.HAND_CONNECTIONS);
    Ac = class extends ec {
      constructor(t2, e2) {
        super(new zu(t2, e2), "image_in", "norm_rect", false), this.landmarks = [], this.worldLandmarks = [], this.handedness = [], Lr(t2 = this.i = new la(), 0, 1, e2 = new Yo()), this.v = new ca(), Lr(this.i, 0, 3, this.v), this.m = new ua(), Lr(this.i, 0, 2, this.m), Dr(this.m, 3, 1), Ur(this.m, 2, 0.5), Ur(this.v, 2, 0.5), Ur(this.i, 4, 0.5);
      }
      D() {
        return "HandLandmarker";
      }
      get baseOptions() {
        return Tr(this.i, Yo, 1);
      }
      set baseOptions(t2) {
        Lr(this.i, 0, 1, t2);
      }
      A(t2) {
        return "numHands" in t2 && Dr(this.m, 3, t2.numHands ?? 1), "minHandDetectionConfidence" in t2 && Ur(this.m, 2, t2.minHandDetectionConfidence ?? 0.5), "minTrackingConfidence" in t2 && Ur(this.i, 4, t2.minTrackingConfidence ?? 0.5), "minHandPresenceConfidence" in t2 && Ur(this.v, 2, t2.minHandPresenceConfidence ?? 0.5), this.l(t2);
      }
      H(t2, e2) {
        return this.landmarks = [], this.worldLandmarks = [], this.handedness = [], Zu(this, t2, e2), Ec(this);
      }
      I(t2, e2, r2) {
        return this.landmarks = [], this.worldLandmarks = [], this.handedness = [], Qu(this, t2, r2, e2), Ec(this);
      }
      u() {
        var t2 = new go();
        po(t2, "image_in"), po(t2, "norm_rect"), mo(t2, "hand_landmarks"), mo(t2, "world_hand_landmarks"), mo(t2, "handedness");
        var e2 = new no();
        hi(e2, _a, this.i);
        var r2 = new ho();
        jr(r2, 2, "mediapipe.tasks.vision.hand_landmarker.HandLandmarkerGraph"), oo(r2, "IMAGE:image_in"), oo(r2, "NORM_RECT:norm_rect"), ao(r2, "LANDMARKS:hand_landmarks"), ao(r2, "WORLD_LANDMARKS:world_hand_landmarks"), ao(r2, "HANDEDNESS:handedness"), r2.A(e2), fo(t2, r2), this.h.attachProtoVectorListener("hand_landmarks", (t3, e3) => {
          for (let e4 of t3) t3 = Io(e4), this.landmarks.push(Qa(t3));
          Zh(this, e3);
        }), this.h.attachEmptyPacketListener("hand_landmarks", (t3) => {
          Zh(this, t3);
        }), this.h.attachProtoVectorListener("world_hand_landmarks", (t3, e3) => {
          for (let e4 of t3) t3 = xo(e4), this.worldLandmarks.push(th(t3));
          Zh(this, e3);
        }), this.h.attachEmptyPacketListener("world_hand_landmarks", (t3) => {
          Zh(this, t3);
        }), this.h.attachProtoVectorListener("handedness", (t3, e3) => {
          var r3 = this.handedness, n2 = r3.push, i2 = [];
          for (let e4 of t3) {
            t3 = wo(e4);
            let r4 = [];
            for (let e5 of t3.h()) r4.push({ score: Nr(e5, 2) ?? 0, index: Cr(e5, 1) ?? 0 ?? -1, categoryName: ve(nr(e5, 3)) ?? "" ?? "", displayName: ve(nr(e5, 4)) ?? "" ?? "" });
            i2.push(r4);
          }
          n2.call(r3, ...i2), Zh(this, e3);
        }), this.h.attachEmptyPacketListener("handedness", (t3) => {
          Zh(this, t3);
        }), t2 = t2.h(), this.setGraph(new Uint8Array(t2), true);
      }
    };
    Ac.prototype.detectForVideo = Ac.prototype.I, Ac.prototype.detect = Ac.prototype.H, Ac.prototype.setOptions = Ac.prototype.A, Ac.createFromModelPath = function(t2, e2) {
      return qu(Ac, t2, { baseOptions: { modelAssetPath: e2 } });
    }, Ac.createFromModelBuffer = function(t2, e2) {
      return qu(Ac, t2, { baseOptions: { modelAssetBuffer: e2 } });
    }, Ac.createFromOptions = function(t2, e2) {
      return qu(Ac, t2, e2);
    }, Ac.HAND_CONNECTIONS = mc, e("module$exports$google3$third_party$mediapipe$tasks$web$vision$hand_landmarker$hand_landmarker.HandLandmarker.HAND_CONNECTIONS", Ac.HAND_CONNECTIONS);
    wc = Xu([0, 1], [1, 2], [2, 3], [3, 7], [0, 4], [4, 5], [5, 6], [6, 8], [9, 10], [11, 12], [11, 13], [13, 15], [15, 17], [15, 19], [15, 21], [17, 19], [12, 14], [14, 16], [16, 18], [16, 20], [16, 22], [18, 20], [11, 23], [12, 24], [23, 24], [23, 25], [24, 26], [25, 27], [26, 28], [27, 29], [28, 30], [29, 31], [30, 32], [27, 31], [28, 32]);
    Sc = class extends ec {
      constructor(t2, e2) {
        super(new zu(t2, e2), "input_frames_image", null, false), this.i = { faceLandmarks: [], faceBlendshapes: [], poseLandmarks: [], poseWorldLandmarks: [], poseSegmentationMasks: [], leftHandLandmarks: [], leftHandWorldLandmarks: [], rightHandLandmarks: [], rightHandWorldLandmarks: [] }, this.outputPoseSegmentationMasks = this.outputFaceBlendshapes = false, Lr(t2 = this.m = new wa(), 0, 1, e2 = new Yo()), this.Z = new ca(), Lr(this.m, 0, 2, this.Z), this.Aa = new ya(), Lr(this.m, 0, 3, this.Aa), this.v = new Zo(), Lr(this.m, 0, 4, this.v), this.P = new ra(), Lr(this.m, 0, 5, this.P), this.C = new Ea(), Lr(this.m, 0, 6, this.C), this.aa = new Aa(), Lr(this.m, 0, 7, this.aa), Ur(this.v, 2, 0.5), Ur(this.v, 3, 0.3), Ur(this.P, 2, 0.5), Ur(this.C, 2, 0.5), Ur(this.C, 3, 0.3), Ur(this.aa, 2, 0.5), Ur(this.Z, 2, 0.5);
      }
      D() {
        return "HolisticLandmarker";
      }
      get baseOptions() {
        return Tr(this.m, Yo, 1);
      }
      set baseOptions(t2) {
        Lr(this.m, 0, 1, t2);
      }
      A(t2) {
        return "minFaceDetectionConfidence" in t2 && Ur(this.v, 2, t2.minFaceDetectionConfidence ?? 0.5), "minFaceSuppressionThreshold" in t2 && Ur(this.v, 3, t2.minFaceSuppressionThreshold ?? 0.3), "minFacePresenceConfidence" in t2 && Ur(this.P, 2, t2.minFacePresenceConfidence ?? 0.5), "outputFaceBlendshapes" in t2 && (this.outputFaceBlendshapes = !!t2.outputFaceBlendshapes), "minPoseDetectionConfidence" in t2 && Ur(this.C, 2, t2.minPoseDetectionConfidence ?? 0.5), "minPoseSuppressionThreshold" in t2 && Ur(this.C, 3, t2.minPoseSuppressionThreshold ?? 0.3), "minPosePresenceConfidence" in t2 && Ur(this.aa, 2, t2.minPosePresenceConfidence ?? 0.5), "outputPoseSegmentationMasks" in t2 && (this.outputPoseSegmentationMasks = !!t2.outputPoseSegmentationMasks), "minHandLandmarksConfidence" in t2 && Ur(this.Z, 2, t2.minHandLandmarksConfidence ?? 0.5), this.l(t2);
      }
      H(t2, e2, r2) {
        var n2 = "function" != typeof e2 ? e2 : {};
        return this.G = "function" == typeof e2 ? e2 : r2, Tc(this), Zu(this, t2, n2), bc(this);
      }
      I(t2, e2, r2, n2) {
        var i2 = "function" != typeof r2 ? r2 : {};
        return this.G = "function" == typeof r2 ? r2 : n2, Tc(this), Qu(this, t2, i2, e2), bc(this);
      }
      u() {
        var t2 = new go();
        po(t2, "input_frames_image"), mo(t2, "pose_landmarks"), mo(t2, "pose_world_landmarks"), mo(t2, "face_landmarks"), mo(t2, "left_hand_landmarks"), mo(t2, "left_hand_world_landmarks"), mo(t2, "right_hand_landmarks"), mo(t2, "right_hand_world_landmarks");
        var e2 = new no(), r2 = new Ps();
        jr(r2, 1, "type.googleapis.com/mediapipe.tasks.vision.holistic_landmarker.proto.HolisticLandmarkerGraphOptions"), (function(t3, e3) {
          if (null != e3) if (Array.isArray(e3)) sr(t3, 2, Ue(e3, 0, je));
          else {
            if (!("string" == typeof e3 || e3 instanceof N || x(e3))) throw Error("invalid value in Any.value field: " + e3 + " expected a ByteString, a base64 encoded string, a Uint8Array or a jspb array");
            mr(t3, 2, lt(e3, false), I());
          }
        })(r2, this.m.h());
        var n2 = new ho();
        jr(n2, 2, "mediapipe.tasks.vision.holistic_landmarker.HolisticLandmarkerGraph"), Fr(n2, 8, Ps, r2), oo(n2, "IMAGE:input_frames_image"), ao(n2, "POSE_LANDMARKS:pose_landmarks"), ao(n2, "POSE_WORLD_LANDMARKS:pose_world_landmarks"), ao(n2, "FACE_LANDMARKS:face_landmarks"), ao(n2, "LEFT_HAND_LANDMARKS:left_hand_landmarks"), ao(n2, "LEFT_HAND_WORLD_LANDMARKS:left_hand_world_landmarks"), ao(n2, "RIGHT_HAND_LANDMARKS:right_hand_landmarks"), ao(n2, "RIGHT_HAND_WORLD_LANDMARKS:right_hand_world_landmarks"), n2.A(e2), fo(t2, n2), Qh(this, t2), this.h.attachProtoListener("pose_landmarks", (t3, e3) => {
          kc(t3, this.i.poseLandmarks), Zh(this, e3);
        }), this.h.attachEmptyPacketListener("pose_landmarks", (t3) => {
          Zh(this, t3);
        }), this.h.attachProtoListener("pose_world_landmarks", (t3, e3) => {
          var r3 = this.i.poseWorldLandmarks;
          t3 = xo(t3), r3.push(th(t3)), Zh(this, e3);
        }), this.h.attachEmptyPacketListener("pose_world_landmarks", (t3) => {
          Zh(this, t3);
        }), this.outputPoseSegmentationMasks && (ao(n2, "POSE_SEGMENTATION_MASK:pose_segmentation_mask"), tu(this, "pose_segmentation_mask"), this.h.ha("pose_segmentation_mask", (t3, e3) => {
          this.i.poseSegmentationMasks = [tc(this, t3, true, !this.G)], Zh(this, e3);
        }), this.h.attachEmptyPacketListener("pose_segmentation_mask", (t3) => {
          this.i.poseSegmentationMasks = [], Zh(this, t3);
        })), this.h.attachProtoListener("face_landmarks", (t3, e3) => {
          kc(t3, this.i.faceLandmarks), Zh(this, e3);
        }), this.h.attachEmptyPacketListener("face_landmarks", (t3) => {
          Zh(this, t3);
        }), this.outputFaceBlendshapes && (mo(t2, "extra_blendshapes"), ao(n2, "FACE_BLENDSHAPES:extra_blendshapes"), this.h.attachProtoListener("extra_blendshapes", (t3, e3) => {
          var r3 = this.i.faceBlendshapes;
          this.outputFaceBlendshapes && (t3 = wo(t3), r3.push(qa(t3.h() ?? []))), Zh(this, e3);
        }), this.h.attachEmptyPacketListener("extra_blendshapes", (t3) => {
          Zh(this, t3);
        })), this.h.attachProtoListener("left_hand_landmarks", (t3, e3) => {
          kc(t3, this.i.leftHandLandmarks), Zh(this, e3);
        }), this.h.attachEmptyPacketListener("left_hand_landmarks", (t3) => {
          Zh(this, t3);
        }), this.h.attachProtoListener("left_hand_world_landmarks", (t3, e3) => {
          var r3 = this.i.leftHandWorldLandmarks;
          t3 = xo(t3), r3.push(th(t3)), Zh(this, e3);
        }), this.h.attachEmptyPacketListener("left_hand_world_landmarks", (t3) => {
          Zh(this, t3);
        }), this.h.attachProtoListener("right_hand_landmarks", (t3, e3) => {
          kc(t3, this.i.rightHandLandmarks), Zh(this, e3);
        }), this.h.attachEmptyPacketListener("right_hand_landmarks", (t3) => {
          Zh(this, t3);
        }), this.h.attachProtoListener("right_hand_world_landmarks", (t3, e3) => {
          var r3 = this.i.rightHandWorldLandmarks;
          t3 = xo(t3), r3.push(th(t3)), Zh(this, e3);
        }), this.h.attachEmptyPacketListener("right_hand_world_landmarks", (t3) => {
          Zh(this, t3);
        }), t2 = t2.h(), this.setGraph(new Uint8Array(t2), true);
      }
    };
    Sc.prototype.detectForVideo = Sc.prototype.I, Sc.prototype.detect = Sc.prototype.H, Sc.prototype.setOptions = Sc.prototype.A, Sc.createFromModelPath = function(t2, e2) {
      return qu(Sc, t2, { baseOptions: { modelAssetPath: e2 } });
    }, Sc.createFromModelBuffer = function(t2, e2) {
      return qu(Sc, t2, { baseOptions: { modelAssetBuffer: e2 } });
    }, Sc.createFromOptions = function(t2, e2) {
      return qu(Sc, t2, e2);
    }, Sc.HAND_CONNECTIONS = mc, e("module$exports$google3$third_party$mediapipe$tasks$web$vision$holistic_landmarker$holistic_landmarker.HolisticLandmarker.HAND_CONNECTIONS", Sc.HAND_CONNECTIONS), Sc.POSE_CONNECTIONS = wc, e("module$exports$google3$third_party$mediapipe$tasks$web$vision$holistic_landmarker$holistic_landmarker.HolisticLandmarker.POSE_CONNECTIONS", Sc.POSE_CONNECTIONS), Sc.FACE_LANDMARKS_LIPS = nc, e("module$exports$google3$third_party$mediapipe$tasks$web$vision$holistic_landmarker$holistic_landmarker.HolisticLandmarker.FACE_LANDMARKS_LIPS", Sc.FACE_LANDMARKS_LIPS), Sc.FACE_LANDMARKS_LEFT_EYE = ic, e("module$exports$google3$third_party$mediapipe$tasks$web$vision$holistic_landmarker$holistic_landmarker.HolisticLandmarker.FACE_LANDMARKS_LEFT_EYE", Sc.FACE_LANDMARKS_LEFT_EYE), Sc.FACE_LANDMARKS_LEFT_EYEBROW = sc, e("module$exports$google3$third_party$mediapipe$tasks$web$vision$holistic_landmarker$holistic_landmarker.HolisticLandmarker.FACE_LANDMARKS_LEFT_EYEBROW", Sc.FACE_LANDMARKS_LEFT_EYEBROW), Sc.FACE_LANDMARKS_LEFT_IRIS = oc, e("module$exports$google3$third_party$mediapipe$tasks$web$vision$holistic_landmarker$holistic_landmarker.HolisticLandmarker.FACE_LANDMARKS_LEFT_IRIS", Sc.FACE_LANDMARKS_LEFT_IRIS), Sc.FACE_LANDMARKS_RIGHT_EYE = ac, e("module$exports$google3$third_party$mediapipe$tasks$web$vision$holistic_landmarker$holistic_landmarker.HolisticLandmarker.FACE_LANDMARKS_RIGHT_EYE", Sc.FACE_LANDMARKS_RIGHT_EYE), Sc.FACE_LANDMARKS_RIGHT_EYEBROW = hc, e("module$exports$google3$third_party$mediapipe$tasks$web$vision$holistic_landmarker$holistic_landmarker.HolisticLandmarker.FACE_LANDMARKS_RIGHT_EYEBROW", Sc.FACE_LANDMARKS_RIGHT_EYEBROW), Sc.FACE_LANDMARKS_RIGHT_IRIS = uc, e("module$exports$google3$third_party$mediapipe$tasks$web$vision$holistic_landmarker$holistic_landmarker.HolisticLandmarker.FACE_LANDMARKS_RIGHT_IRIS", Sc.FACE_LANDMARKS_RIGHT_IRIS), Sc.FACE_LANDMARKS_FACE_OVAL = cc, e("module$exports$google3$third_party$mediapipe$tasks$web$vision$holistic_landmarker$holistic_landmarker.HolisticLandmarker.FACE_LANDMARKS_FACE_OVAL", Sc.FACE_LANDMARKS_FACE_OVAL), Sc.FACE_LANDMARKS_CONTOURS = lc, e("module$exports$google3$third_party$mediapipe$tasks$web$vision$holistic_landmarker$holistic_landmarker.HolisticLandmarker.FACE_LANDMARKS_CONTOURS", Sc.FACE_LANDMARKS_CONTOURS), Sc.FACE_LANDMARKS_TESSELATION = dc, e("module$exports$google3$third_party$mediapipe$tasks$web$vision$holistic_landmarker$holistic_landmarker.HolisticLandmarker.FACE_LANDMARKS_TESSELATION", Sc.FACE_LANDMARKS_TESSELATION);
    Lc = class extends ec {
      constructor(t2, e2) {
        super(new zu(t2, e2), "input_image", "norm_rect", true), this.m = { classifications: [] }, Lr(t2 = this.i = new ka(), 0, 1, e2 = new Yo());
      }
      D() {
        return "ImageClassifier";
      }
      get baseOptions() {
        return Tr(this.i, Yo, 1);
      }
      set baseOptions(t2) {
        Lr(this.i, 0, 1, t2);
      }
      A(t2) {
        return Lr(this.i, 0, 2, Ya(t2, Tr(this.i, jo, 2))), this.l(t2);
      }
      Ga(t2, e2) {
        return this.m = { classifications: [] }, Zu(this, t2, e2), this.m;
      }
      Ha(t2, e2, r2) {
        return this.m = { classifications: [] }, Qu(this, t2, r2, e2), this.m;
      }
      u() {
        var t2 = new go();
        po(t2, "input_image"), po(t2, "norm_rect"), mo(t2, "classifications");
        var e2 = new no();
        hi(e2, Sa, this.i);
        var r2 = new ho();
        jr(r2, 2, "mediapipe.tasks.vision.image_classifier.ImageClassifierGraph"), oo(r2, "IMAGE:input_image"), oo(r2, "NORM_RECT:norm_rect"), ao(r2, "CLASSIFICATIONS:classifications"), r2.A(e2), fo(t2, r2), this.h.attachProtoListener("classifications", (t3, e3) => {
          this.m = Ja(Mo(t3)), Zh(this, e3);
        }), this.h.attachEmptyPacketListener("classifications", (t3) => {
          Zh(this, t3);
        }), t2 = t2.h(), this.setGraph(new Uint8Array(t2), true);
      }
    };
    Lc.prototype.classifyForVideo = Lc.prototype.Ha, Lc.prototype.classify = Lc.prototype.Ga, Lc.prototype.setOptions = Lc.prototype.A, Lc.createFromModelPath = function(t2, e2) {
      return qu(Lc, t2, { baseOptions: { modelAssetPath: e2 } });
    }, Lc.createFromModelBuffer = function(t2, e2) {
      return qu(Lc, t2, { baseOptions: { modelAssetBuffer: e2 } });
    }, Lc.createFromOptions = function(t2, e2) {
      return qu(Lc, t2, e2);
    };
    xc = class extends ec {
      constructor(t2, e2) {
        super(new zu(t2, e2), "image_in", "norm_rect", true), this.i = new La(), this.embeddings = { embeddings: [] }, Lr(t2 = this.i, 0, 1, e2 = new Yo());
      }
      D() {
        return "ImageEmbedder";
      }
      get baseOptions() {
        return Tr(this.i, Yo, 1);
      }
      set baseOptions(t2) {
        Lr(this.i, 0, 1, t2);
      }
      A(t2) {
        var e2 = this.i, r2 = Tr(this.i, Vo, 2);
        if (r2 = r2 ? r2.clone() : new Vo(), void 0 !== t2.l2Normalize) {
          var n2 = t2.l2Normalize;
          sr(r2, 1, null == n2 ? n2 : Zt(n2));
        } else "l2Normalize" in t2 && sr(r2, 1);
        return void 0 !== t2.quantize ? sr(r2, 2, null == (n2 = t2.quantize) ? n2 : Zt(n2)) : "quantize" in t2 && sr(r2, 2), Lr(e2, 0, 2, r2), this.l(t2);
      }
      Na(t2, e2) {
        return Zu(this, t2, e2), this.embeddings;
      }
      Oa(t2, e2, r2) {
        return Qu(this, t2, r2, e2), this.embeddings;
      }
      u() {
        var t2 = new go();
        po(t2, "image_in"), po(t2, "norm_rect"), mo(t2, "embeddings_out");
        var e2 = new no();
        hi(e2, xa, this.i);
        var r2 = new ho();
        jr(r2, 2, "mediapipe.tasks.vision.image_embedder.ImageEmbedderGraph"), oo(r2, "IMAGE:image_in"), oo(r2, "NORM_RECT:norm_rect"), ao(r2, "EMBEDDINGS:embeddings_out"), r2.A(e2), fo(t2, r2), this.h.attachProtoListener("embeddings_out", (t3, e3) => {
          t3 = Bo(t3), this.embeddings = (function(t4) {
            return { embeddings: kr(t4, $o, 1).map((t5) => {
              var e4 = { headIndex: Cr(t5, 3) ?? 0 ?? -1, headName: ve(nr(t5, 4)) ?? "" ?? "" };
              if (hr(t5, 1, Uo)) t5 = ur(t5 = Or(t5, Do, 1, Uo), 1, Jt), e4.floatEmbedding = t5.slice();
              else {
                let r3 = new Uint8Array(0);
                e4.quantizedEmbedding = Or(t5, Po, 2, Uo)?.h()?.i() ?? r3;
              }
              return e4;
            }), timestampMs: za(nr(t4, 2, void 0, le) ?? er) };
          })(t3), Zh(this, e3);
        }), this.h.attachEmptyPacketListener("embeddings_out", (t3) => {
          Zh(this, t3);
        }), t2 = t2.h(), this.setGraph(new Uint8Array(t2), true);
      }
    };
    xc.cosineSimilarity = function(t2, e2) {
      if (t2.floatEmbedding && e2.floatEmbedding) t2 = rh(t2.floatEmbedding, e2.floatEmbedding);
      else {
        if (!t2.quantizedEmbedding || !e2.quantizedEmbedding) throw Error("Cannot compute cosine similarity between quantized and float embeddings.");
        t2 = rh(eh(t2.quantizedEmbedding), eh(e2.quantizedEmbedding));
      }
      return t2;
    }, xc.prototype.embedForVideo = xc.prototype.Oa, xc.prototype.embed = xc.prototype.Na, xc.prototype.setOptions = xc.prototype.A, xc.createFromModelPath = function(t2, e2) {
      return qu(xc, t2, { baseOptions: { modelAssetPath: e2 } });
    }, xc.createFromModelBuffer = function(t2, e2) {
      return qu(xc, t2, { baseOptions: { modelAssetBuffer: e2 } });
    }, xc.createFromOptions = function(t2, e2) {
      return qu(xc, t2, e2);
    };
    Rc = class {
      constructor(t2, e2, r2) {
        this.confidenceMasks = t2, this.categoryMask = e2, this.qualityScores = r2;
      }
      close() {
        this.confidenceMasks?.forEach((t2) => {
          t2.close();
        }), this.categoryMask?.close();
      }
    };
    Rc.prototype.close = Rc.prototype.close;
    Nc = class extends ec {
      constructor(t2, e2) {
        super(new zu(t2, e2), "image_in", "norm_rect", false), this.v = [], this.outputCategoryMask = false, this.outputConfidenceMasks = true, this.i = new Na(), this.C = new Ra(), Lr(this.i, 0, 3, this.C), Lr(t2 = this.i, 0, 1, e2 = new Yo());
      }
      D() {
        return "ImageSegmenter";
      }
      get baseOptions() {
        return Tr(this.i, Yo, 1);
      }
      set baseOptions(t2) {
        Lr(this.i, 0, 1, t2);
      }
      A(t2) {
        return void 0 !== t2.displayNamesLocale ? sr(this.i, 2, ge(t2.displayNamesLocale)) : "displayNamesLocale" in t2 && sr(this.i, 2), "outputCategoryMask" in t2 && (this.outputCategoryMask = t2.outputCategoryMask ?? false), "outputConfidenceMasks" in t2 && (this.outputConfidenceMasks = t2.outputConfidenceMasks ?? true), super.l(t2);
      }
      M() {
        Ic(this);
      }
      segment(t2, e2, r2) {
        var n2 = "function" != typeof e2 ? e2 : {};
        return this.m = "function" == typeof e2 ? e2 : r2, Fc(this), Zu(this, t2, n2), Cc(this);
      }
      cb(t2, e2, r2, n2) {
        var i2 = "function" != typeof r2 ? r2 : {};
        return this.m = "function" == typeof r2 ? r2 : n2, Fc(this), Qu(this, t2, i2, e2), Cc(this);
      }
      Ra() {
        return this.v;
      }
      u() {
        var t2 = new go();
        po(t2, "image_in"), po(t2, "norm_rect");
        var e2 = new no();
        hi(e2, Oa, this.i);
        var r2 = new ho();
        jr(r2, 2, "mediapipe.tasks.vision.image_segmenter.ImageSegmenterGraph"), oo(r2, "IMAGE:image_in"), oo(r2, "NORM_RECT:norm_rect"), r2.A(e2), fo(t2, r2), Qh(this, t2), this.outputConfidenceMasks && (mo(t2, "confidence_masks"), ao(r2, "CONFIDENCE_MASKS:confidence_masks"), tu(this, "confidence_masks"), this.h.ia("confidence_masks", (t3, e3) => {
          this.confidenceMasks = t3.map((t4) => tc(this, t4, true, !this.m)), Zh(this, e3);
        }), this.h.attachEmptyPacketListener("confidence_masks", (t3) => {
          this.confidenceMasks = [], Zh(this, t3);
        })), this.outputCategoryMask && (mo(t2, "category_mask"), ao(r2, "CATEGORY_MASK:category_mask"), tu(this, "category_mask"), this.h.ha("category_mask", (t3, e3) => {
          this.categoryMask = tc(this, t3, false, !this.m), Zh(this, e3);
        }), this.h.attachEmptyPacketListener("category_mask", (t3) => {
          this.categoryMask = void 0, Zh(this, t3);
        })), mo(t2, "quality_scores"), ao(r2, "QUALITY_SCORES:quality_scores"), this.h.attachFloatVectorListener("quality_scores", (t3, e3) => {
          this.qualityScores = t3, Zh(this, e3);
        }), this.h.attachEmptyPacketListener("quality_scores", (t3) => {
          this.categoryMask = void 0, Zh(this, t3);
        }), t2 = t2.h(), this.setGraph(new Uint8Array(t2), true);
      }
    };
    Nc.prototype.getLabels = Nc.prototype.Ra, Nc.prototype.segmentForVideo = Nc.prototype.cb, Nc.prototype.segment = Nc.prototype.segment, Nc.prototype.setOptions = Nc.prototype.A, Nc.createFromModelPath = function(t2, e2) {
      return qu(Nc, t2, { baseOptions: { modelAssetPath: e2 } });
    }, Nc.createFromModelBuffer = function(t2, e2) {
      return qu(Nc, t2, { baseOptions: { modelAssetBuffer: e2 } });
    }, Nc.createFromOptions = function(t2, e2) {
      return qu(Nc, t2, e2);
    };
    Oc = { 0: 0, 1: 1, 2: 2, 3: 3 };
    Dc = class extends ru {
      constructor(t2, e2) {
        super(new Xh(t2, e2)), this.v = new du(), this.delegate = "CPU", this.i = 0, this.baseOptions = new Yo(), this.C = this.m = 0;
      }
      D() {
        return "InteractiveSegmenter";
      }
      get j() {
        return this.h.j;
      }
      A(t2) {
        return this.delegate = t2.baseOptions?.delegate ?? "CPU", super.l(t2);
      }
      eb(t2) {
        if (0 === this.i) throw Error("Segmenter is not initialized.");
        var e2;
        if (0 !== this.m && (this.j._free(this.m), this.m = 0), !(e2 = "undefined" != typeof ImageData && t2 instanceof ImageData)) if ("object" != typeof t2 || null === t2) e2 = false;
        else {
          e2 = t2.data;
          var r2 = t2.width, n2 = t2.height;
          e2 = Number.isInteger(r2) && r2 > 0 && Number.isInteger(n2) && n2 > 0 && (e2 instanceof Uint8ClampedArray || e2 instanceof Uint8Array);
        }
        if (e2) e2 = t2.width, r2 = t2.height, t2 = t2.data;
        else {
          if ([e2, r2] = Dh(t2), "undefined" != typeof OffscreenCanvas) n2 = new OffscreenCanvas(e2, r2);
          else {
            if ("undefined" == typeof document) throw Error("Canvas is not supported in this environment.");
            n2 = document.createElement("canvas");
          }
          if (n2.width = e2, n2.height = r2, !(n2 = n2.getContext("2d"))) throw Error("Canvas 2D context is not supported in this environment.");
          n2.drawImage(t2, 0, 0), t2 = n2.getImageData(0, 0, e2, r2).data;
        }
        if (!t2) throw Error("Unsupported image source or failed to extract image pixels.");
        n2 = (function({ Wa: t3, width: e3, height: r3 }) {
          if (e3 <= 0 || r3 <= 0) throw Error(`Invalid image dimensions: ${e3}x${r3}. Dimensions must be positive.`);
          if (t3 % (e3 * r3) !== 0) throw Error(`Invalid image dimensions or pixel data length. Pixel data length ${t3} is not a multiple of the number of pixels (${e3 * r3}).`);
          if (4 !== (t3 /= e3 * r3) && 3 !== t3 && 1 !== t3) throw Error(`Invalid image dimensions or pixel data length. Calculated channels: ${t3}. Expected 1, 3, or 4.`);
          return t3;
        })({ Wa: t2.length, width: e2, height: r2 });
        var i2 = this.j._malloc(t2.length);
        if (this.j.HEAPU8.set(t2, i2), this.m = i2, !this.j._interactive_segmenter_set_image(this.i, i2, e2, r2, n2)) throw Error("Failed to set image on native engine.");
      }
      segment(t2) {
        if (0 === this.i) throw Error("Segmenter is not initialized.");
        var e2 = (function(t3) {
          t3 = t3.map(({ isCompleted: t4, brushMode: e4, point: r3 }) => {
            e4 = Oc[e4] ?? 0, r3 = r3.map(({ x: t5, y: e5 }) => {
              var r4 = new Ma();
              return Br(r4, 1, t5), Br(r4, 2, e5), r4;
            });
            var n3 = new Da();
            return Mr(n3, t4), mr(n3, 1, ee(e4), 0), Rr(n3, 2, r3), n3;
          });
          var e3 = new Pa();
          return Rr(e3, 1, t3), $a(e3);
        })(t2);
        t2 = this.j._malloc(e2.length), this.j.HEAPU8.set(e2, t2);
        var r2 = this.j._malloc(12), n2 = r2 + 4, i2 = r2 + 8, s2 = 0, o2 = this.C++;
        try {
          if (this.o) if ("GPU" === this.delegate) {
            var a2 = this.o;
            ++a2.h.U, a2.i.set(o2, performance.now());
          } else {
            var h2 = this.o;
            ++h2.h.R, h2.i.set(o2, performance.now());
          }
          if (0 === (s2 = this.j._interactive_segmenter_segment(this.i, t2, e2.length, r2, n2, i2))) throw Error("Segmentation failed.");
          this.o?.za(o2);
          let u2 = this.j.HEAPU32[r2 / 4], c2 = this.j.HEAPU32[n2 / 4], l2 = new Float32Array(this.j.HEAPU8.buffer, s2, this.j.HEAPU32[i2 / 4] / 4), d2 = new Float32Array(l2);
          if (a2 = u2 * c2, (d2 instanceof Uint8Array || d2 instanceof Float32Array) && d2.length !== a2) throw Error("Unsupported channel count: " + d2.length / a2);
          return new bu([d2], true, false, this.h.j.canvas ?? void 0, this.v, u2, c2);
        } finally {
          0 !== t2 && this.j._free(t2), 0 !== r2 && this.j._free(r2), 0 !== s2 && this.j._free(s2);
        }
      }
      u() {
        0 !== this.i && (this.o?.xa(), this.j._interactive_segmenter_close(this.i), this.i = 0), 0 !== this.m && (this.j._free(this.m), this.m = 0);
        var t2 = new Ko();
        if ("GPU" === this.delegate) {
          var e2 = new Xs();
          xr(t2, 2, Xo, e2);
        } else Dr(e2 = new eo(), 1, 4), xr(t2, 1, Xo, e2);
        if (Lr(this.baseOptions, 0, 3, t2), t2 = Jo(this.baseOptions), e2 = this.j._malloc(t2.length), this.j.HEAPU8.set(t2, e2), this.i = this.j._interactive_segmenter_create(e2, t2.length), this.j._free(e2), 0 === this.i) throw Error("Failed to create native InteractiveSegmenter engine.");
        this.o?.ya();
      }
      close() {
        0 !== this.i && (this.j._interactive_segmenter_close(this.i), this.i = 0), 0 !== this.m && (this.j._free(this.m), this.m = 0), this.v.close(), super.close();
      }
    };
    Dc.prototype.close = Dc.prototype.close, Dc.prototype.segment = Dc.prototype.segment, Dc.prototype.setImage = Dc.prototype.eb, Dc.prototype.setOptions = Dc.prototype.A, Dc.createFromModelPath = function(t2, e2) {
      return Yh(Dc, Mc(), t2, { baseOptions: { modelAssetPath: e2 } });
    }, Dc.createFromModelBuffer = function(t2, e2) {
      return Yh(Dc, Mc(), t2, { baseOptions: { modelAssetBuffer: e2 } });
    }, Dc.createFromOptions = function(t2, e2) {
      var r2 = e2.canvas ?? Mc();
      return Yh(Dc, r2, t2, e2);
    };
    Pc = class {
      constructor(t2, e2, r2) {
        this.confidenceMasks = t2, this.categoryMask = e2, this.qualityScores = r2;
      }
      close() {
        this.confidenceMasks?.forEach((t2) => {
          t2.close();
        }), this.categoryMask?.close();
      }
    };
    Pc.prototype.close = Pc.prototype.close;
    $c = class extends ec {
      constructor(t2, e2) {
        super(new zu(t2, e2), "image_in", "norm_rect_in", false), this.outputCategoryMask = false, this.outputConfidenceMasks = true, this.i = new Na(), this.v = new Ra(), Lr(this.i, 0, 3, this.v), Lr(t2 = this.i, 0, 1, e2 = new Yo());
      }
      D() {
        return "InteractiveSegmenterLegacy";
      }
      get baseOptions() {
        return Tr(this.i, Yo, 1);
      }
      set baseOptions(t2) {
        Lr(this.i, 0, 1, t2);
      }
      A(t2) {
        return "outputCategoryMask" in t2 && (this.outputCategoryMask = t2.outputCategoryMask ?? false), "outputConfidenceMasks" in t2 && (this.outputConfidenceMasks = t2.outputConfidenceMasks ?? true), super.l(t2);
      }
      segment(t2, e2, r2, n2) {
        var i2 = "function" != typeof r2 ? r2 : {};
        if (this.m = "function" == typeof r2 ? r2 : n2, this.qualityScores = this.categoryMask = this.confidenceMasks = void 0, r2 = this.J + 1, n2 = new Ga(), e2.keypoint && e2.scribble) throw Error("Cannot provide both keypoint and scribble.");
        if (e2.keypoint) {
          var s2 = new Ua();
          Mr(s2, true), Br(s2, 1, e2.keypoint.x), Br(s2, 2, e2.keypoint.y), xr(n2, 1, Va, s2);
        } else {
          if (!e2.scribble) throw Error("Must provide either a keypoint or a scribble.");
          {
            let t3 = new ja();
            for (s2 of e2.scribble) Mr(e2 = new Ua(), true), Br(e2, 1, s2.x), Br(e2, 2, s2.y), Fr(t3, 1, Ua, e2);
            xr(n2, 2, Va, t3);
          }
        }
        this.h.addProtoToStream(n2.h(), "mediapipe.tasks.vision.interactive_segmenter_legacy.proto.RegionOfInterest", "roi_in", r2), Zu(this, t2, i2);
        t: {
          try {
            let t3 = new Pc(this.confidenceMasks, this.categoryMask, this.qualityScores);
            if (!this.m) {
              var o2 = t3;
              break t;
            }
            this.m(t3);
          } finally {
            eu(this);
          }
          o2 = void 0;
        }
        return o2;
      }
      u() {
        var t2 = new go();
        po(t2, "image_in"), po(t2, "roi_in"), po(t2, "norm_rect_in");
        var e2 = new no();
        hi(e2, Oa, this.i);
        var r2 = new ho();
        jr(r2, 2, "mediapipe.tasks.vision.interactive_segmenter_legacy.InteractiveSegmenterLegacyGraph"), oo(r2, "IMAGE:image_in"), oo(r2, "ROI:roi_in"), oo(r2, "NORM_RECT:norm_rect_in"), r2.A(e2), fo(t2, r2), Qh(this, t2), this.outputConfidenceMasks && (mo(t2, "confidence_masks"), ao(r2, "CONFIDENCE_MASKS:confidence_masks"), tu(this, "confidence_masks"), this.h.ia("confidence_masks", (t3, e3) => {
          this.confidenceMasks = t3.map((t4) => tc(this, t4, true, !this.m)), Zh(this, e3);
        }), this.h.attachEmptyPacketListener("confidence_masks", (t3) => {
          this.confidenceMasks = [], Zh(this, t3);
        })), this.outputCategoryMask && (mo(t2, "category_mask"), ao(r2, "CATEGORY_MASK:category_mask"), tu(this, "category_mask"), this.h.ha("category_mask", (t3, e3) => {
          this.categoryMask = tc(this, t3, false, !this.m), Zh(this, e3);
        }), this.h.attachEmptyPacketListener("category_mask", (t3) => {
          this.categoryMask = void 0, Zh(this, t3);
        })), mo(t2, "quality_scores"), ao(r2, "QUALITY_SCORES:quality_scores"), this.h.attachFloatVectorListener("quality_scores", (t3, e3) => {
          this.qualityScores = t3, Zh(this, e3);
        }), this.h.attachEmptyPacketListener("quality_scores", (t3) => {
          this.categoryMask = void 0, Zh(this, t3);
        }), t2 = t2.h(), this.setGraph(new Uint8Array(t2), true);
      }
    };
    $c.prototype.segment = $c.prototype.segment, $c.prototype.setOptions = $c.prototype.A, $c.createFromModelPath = function(t2, e2) {
      return qu($c, t2, { baseOptions: { modelAssetPath: e2 } });
    }, $c.createFromModelBuffer = function(t2, e2) {
      return qu($c, t2, { baseOptions: { modelAssetBuffer: e2 } });
    }, $c.createFromOptions = function(t2, e2) {
      return qu($c, t2, e2);
    };
    Uc = class extends ec {
      constructor(t2, e2) {
        super(new zu(t2, e2), "input_frame_gpu", "norm_rect", false), this.m = { detections: [] }, Lr(t2 = this.i = new Ha(), 0, 1, e2 = new Yo());
      }
      D() {
        return "ObjectDetector";
      }
      get baseOptions() {
        return Tr(this.i, Yo, 1);
      }
      set baseOptions(t2) {
        Lr(this.i, 0, 1, t2);
      }
      A(t2) {
        return void 0 !== t2.displayNamesLocale ? sr(this.i, 2, ge(t2.displayNamesLocale)) : "displayNamesLocale" in t2 && sr(this.i, 2), void 0 !== t2.maxResults ? Dr(this.i, 3, t2.maxResults) : "maxResults" in t2 && sr(this.i, 3), void 0 !== t2.scoreThreshold ? Ur(this.i, 4, t2.scoreThreshold) : "scoreThreshold" in t2 && sr(this.i, 4), void 0 !== t2.categoryAllowlist ? Gr(this.i, 5, t2.categoryAllowlist) : "categoryAllowlist" in t2 && sr(this.i, 5), void 0 !== t2.categoryDenylist ? Gr(this.i, 6, t2.categoryDenylist) : "categoryDenylist" in t2 && sr(this.i, 6), this.l(t2);
      }
      H(t2, e2) {
        return this.m = { detections: [] }, Zu(this, t2, e2), this.m;
      }
      I(t2, e2, r2) {
        return this.m = { detections: [] }, Qu(this, t2, r2, e2), this.m;
      }
      u() {
        var t2 = new go();
        po(t2, "input_frame_gpu"), po(t2, "norm_rect"), mo(t2, "detections");
        var e2 = new no();
        hi(e2, Ka, this.i);
        var r2 = new ho();
        jr(r2, 2, "mediapipe.tasks.vision.ObjectDetectorGraph"), oo(r2, "IMAGE:input_frame_gpu"), oo(r2, "NORM_RECT:norm_rect"), ao(r2, "DETECTIONS:detections"), r2.A(e2), fo(t2, r2), this.h.attachProtoVectorListener("detections", (t3, e3) => {
          for (let e4 of t3) t3 = So(e4), this.m.detections.push(Za(t3));
          Zh(this, e3);
        }), this.h.attachEmptyPacketListener("detections", (t3) => {
          Zh(this, t3);
        }), t2 = t2.h(), this.setGraph(new Uint8Array(t2), true);
      }
    };
    Uc.prototype.detectForVideo = Uc.prototype.I, Uc.prototype.detect = Uc.prototype.H, Uc.prototype.setOptions = Uc.prototype.A, Uc.createFromModelPath = async function(t2, e2) {
      return qu(Uc, t2, { baseOptions: { modelAssetPath: e2 } });
    }, Uc.createFromModelBuffer = function(t2, e2) {
      return qu(Uc, t2, { baseOptions: { modelAssetBuffer: e2 } });
    }, Uc.createFromOptions = function(t2, e2) {
      return qu(Uc, t2, e2);
    };
    Bc = class {
      constructor(t2, e2, r2) {
        this.landmarks = t2, this.worldLandmarks = e2, this.segmentationMasks = r2;
      }
      close() {
        this.segmentationMasks?.forEach((t2) => {
          t2.close();
        });
      }
    };
    Bc.prototype.close = Bc.prototype.close;
    Vc = class extends ec {
      constructor(t2, e2) {
        super(new zu(t2, e2), "image_in", "norm_rect", false), this.landmarks = [], this.worldLandmarks = [], this.outputSegmentationMasks = false, Lr(t2 = this.i = new Xa(), 0, 1, e2 = new Yo()), this.C = new Aa(), Lr(this.i, 0, 3, this.C), this.m = new Ea(), Lr(this.i, 0, 2, this.m), Dr(this.m, 4, 1), Ur(this.m, 2, 0.5), Ur(this.C, 2, 0.5), Ur(this.i, 4, 0.5);
      }
      D() {
        return "PoseLandmarker";
      }
      get baseOptions() {
        return Tr(this.i, Yo, 1);
      }
      set baseOptions(t2) {
        Lr(this.i, 0, 1, t2);
      }
      A(t2) {
        return "numPoses" in t2 && Dr(this.m, 4, t2.numPoses ?? 1), "minPoseDetectionConfidence" in t2 && Ur(this.m, 2, t2.minPoseDetectionConfidence ?? 0.5), "minTrackingConfidence" in t2 && Ur(this.i, 4, t2.minTrackingConfidence ?? 0.5), "minPosePresenceConfidence" in t2 && Ur(this.C, 2, t2.minPosePresenceConfidence ?? 0.5), "outputSegmentationMasks" in t2 && (this.outputSegmentationMasks = t2.outputSegmentationMasks ?? false), this.l(t2);
      }
      H(t2, e2, r2) {
        var n2 = "function" != typeof e2 ? e2 : {};
        return this.v = "function" == typeof e2 ? e2 : r2, jc(this), Zu(this, t2, n2), Gc(this);
      }
      I(t2, e2, r2, n2) {
        var i2 = "function" != typeof r2 ? r2 : {};
        return this.v = "function" == typeof r2 ? r2 : n2, jc(this), Qu(this, t2, i2, e2), Gc(this);
      }
      u() {
        var t2 = new go();
        po(t2, "image_in"), po(t2, "norm_rect"), mo(t2, "normalized_landmarks"), mo(t2, "world_landmarks"), mo(t2, "segmentation_masks");
        var e2 = new no();
        hi(e2, Wa, this.i);
        var r2 = new ho();
        jr(r2, 2, "mediapipe.tasks.vision.pose_landmarker.PoseLandmarkerGraph"), oo(r2, "IMAGE:image_in"), oo(r2, "NORM_RECT:norm_rect"), ao(r2, "NORM_LANDMARKS:normalized_landmarks"), ao(r2, "WORLD_LANDMARKS:world_landmarks"), r2.A(e2), fo(t2, r2), Qh(this, t2), this.h.attachProtoVectorListener("normalized_landmarks", (t3, e3) => {
          this.landmarks = [];
          for (let e4 of t3) t3 = Io(e4), this.landmarks.push(Qa(t3));
          Zh(this, e3);
        }), this.h.attachEmptyPacketListener("normalized_landmarks", (t3) => {
          this.landmarks = [], Zh(this, t3);
        }), this.h.attachProtoVectorListener("world_landmarks", (t3, e3) => {
          this.worldLandmarks = [];
          for (let e4 of t3) t3 = xo(e4), this.worldLandmarks.push(th(t3));
          Zh(this, e3);
        }), this.h.attachEmptyPacketListener("world_landmarks", (t3) => {
          this.worldLandmarks = [], Zh(this, t3);
        }), this.outputSegmentationMasks && (ao(r2, "SEGMENTATION_MASK:segmentation_masks"), tu(this, "segmentation_masks"), this.h.ia("segmentation_masks", (t3, e3) => {
          this.segmentationMasks = t3.map((t4) => tc(this, t4, true, !this.v)), Zh(this, e3);
        }), this.h.attachEmptyPacketListener("segmentation_masks", (t3) => {
          this.segmentationMasks = [], Zh(this, t3);
        })), t2 = t2.h(), this.setGraph(new Uint8Array(t2), true);
      }
    };
    Vc.prototype.detectForVideo = Vc.prototype.I, Vc.prototype.detect = Vc.prototype.H, Vc.prototype.setOptions = Vc.prototype.A, Vc.createFromModelPath = function(t2, e2) {
      return qu(Vc, t2, { baseOptions: { modelAssetPath: e2 } });
    }, Vc.createFromModelBuffer = function(t2, e2) {
      return qu(Vc, t2, { baseOptions: { modelAssetBuffer: e2 } });
    }, Vc.createFromOptions = function(t2, e2) {
      return qu(Vc, t2, e2);
    }, Vc.POSE_CONNECTIONS = wc, e("module$exports$google3$third_party$mediapipe$tasks$web$vision$pose_landmarker$pose_landmarker.PoseLandmarker.POSE_CONNECTIONS", Vc.POSE_CONNECTIONS);
  }
});

// src/cameraOptions.ts
var MEDIAPIPE_VERSION = "1.2.3";
var DEFAULT_WASM_URL = `https://cdn.jsdelivr.net/npm/@mediapipe/tasks-vision@${MEDIAPIPE_VERSION}/wasm`;
var DEFAULT_MODEL_URL = "https://storage.googleapis.com/mediapipe-models/face_landmarker/face_landmarker/float16/1/face_landmarker.task";
var DEFAULT_CAMERA = {
  facingMode: "user",
  width: 1280,
  height: 720,
  frameRate: 30,
  wasmBaseUrl: DEFAULT_WASM_URL,
  modelUrl: DEFAULT_MODEL_URL,
  delegate: "GPU",
  numFaces: 1,
  measureLight: false
};

// src/webcam.ts
var WebcamCore = class {
  options;
  video = null;
  stream = null;
  landmarker = null;
  ownsVideo = false;
  running = false;
  starting = null;
  holders = 0;
  lastVideoTime = -1;
  ticker = null;
  handlers = /* @__PURE__ */ new Set();
  lumaCanvas = null;
  lumaAt = 0;
  luma;
  constructor(options = {}) {
    this.options = { ...DEFAULT_CAMERA, ...options };
  }
  get isRunning() {
    return this.running;
  }
  /** Receive every frame. Returns the function that unsubscribes. Does not start the camera. */
  subscribe(handler) {
    this.handlers.add(handler);
    return () => this.handlers.delete(handler);
  }
  /** Start the camera if it is not running and count one more user of it. */
  async retain() {
    this.holders++;
    try {
      await this.start();
    } catch (err) {
      this.holders = Math.max(0, this.holders - 1);
      throw err;
    }
  }
  /** Let go; the camera stops when the last holder does. */
  release() {
    this.holders = Math.max(0, this.holders - 1);
    if (this.holders === 0) this.stop();
  }
  /** The cameras the browser knows about. Labels are empty until camera permission is granted. */
  static async listCameras() {
    const devices = await navigator.mediaDevices.enumerateDevices();
    return devices.filter((d2) => d2.kind === "videoinput");
  }
  /** Whether the page may use the camera, without prompting. "unknown" where the browser cannot say. */
  static async permission() {
    try {
      const status = await navigator.permissions.query({ name: "camera" });
      return status.state;
    } catch {
      return "unknown";
    }
  }
  /** Switch to another camera; restarts the stream if the core is running. */
  async selectDevice(deviceId) {
    this.options.deviceId = deviceId;
    if (!this.running) return;
    const was = this.holders;
    this.stop();
    this.holders = was;
    await this.start();
  }
  async start() {
    if (this.running) return;
    if (this.starting) return this.starting;
    this.starting = this.open().finally(() => {
      this.starting = null;
    });
    return this.starting;
  }
  async open() {
    const o2 = this.options;
    if (!navigator.mediaDevices?.getUserMedia) {
      throw new Error("This page cannot use the camera. Open it over https:// or on localhost.");
    }
    this.stream = await navigator.mediaDevices.getUserMedia({
      audio: false,
      video: {
        ...o2.deviceId ? { deviceId: { exact: o2.deviceId } } : { facingMode: o2.facingMode },
        width: { ideal: o2.width },
        height: { ideal: o2.height },
        frameRate: { ideal: o2.frameRate }
      }
    });
    let video = o2.video ?? null;
    if (!video) {
      video = document.createElement("video");
      video.style.cssText = "position:fixed;width:1px;height:1px;opacity:0;pointer-events:none;left:0;top:0";
      document.body.appendChild(video);
      this.ownsVideo = true;
    }
    video.playsInline = true;
    video.muted = true;
    video.srcObject = this.stream;
    this.video = video;
    await video.play();
    const vision = await Promise.resolve().then(() => (init_vision_bundle(), vision_bundle_exports));
    const fileset = await vision.FilesetResolver.forVisionTasks(o2.wasmBaseUrl);
    const create = (delegate) => vision.FaceLandmarker.createFromOptions(fileset, {
      baseOptions: { modelAssetPath: o2.modelUrl, delegate },
      runningMode: "VIDEO",
      numFaces: Math.max(1, o2.numFaces),
      // The position and direction of the head in camera space: what lets the gaze model allow
      // for where the camera is and for head movement.
      outputFacialTransformationMatrixes: true,
      minFaceDetectionConfidence: 0.5,
      minFacePresenceConfidence: 0.5,
      minTrackingConfidence: 0.5
    });
    try {
      this.landmarker = await create(o2.delegate);
    } catch (err) {
      if (o2.delegate !== "GPU") throw err;
      this.landmarker = await create("CPU");
    }
    this.running = true;
    const loop = () => {
      if (!this.running) return;
      this.process();
      const v2 = this.video;
      if (v2?.requestVideoFrameCallback) v2.requestVideoFrameCallback(loop);
      else requestAnimationFrame(loop);
    };
    loop();
    try {
      const code = "setInterval(() => postMessage(0), 33);";
      this.ticker = new Worker(URL.createObjectURL(new Blob([code], { type: "text/javascript" })));
      this.ticker.onmessage = () => {
        if (document.hidden) this.process();
      };
    } catch {
    }
  }
  process() {
    if (!this.running || !this.video || !this.landmarker) return;
    const v2 = this.video;
    if (v2.readyState < 2 || v2.videoWidth === 0 || v2.currentTime === this.lastVideoTime) return;
    this.lastVideoTime = v2.currentTime;
    const t2 = performance.now();
    const faces = [];
    try {
      const result = this.landmarker.detectForVideo(v2, t2);
      const aspect = v2.videoWidth / v2.videoHeight;
      (result.faceLandmarks ?? []).forEach((landmarks, i2) => {
        faces.push({ landmarks, aspect, matrix: result.facialTransformationMatrixes?.[i2]?.data ?? null, t: t2 });
      });
    } catch {
    }
    const frame = { t: t2, faces };
    if (this.options.measureLight) {
      if (t2 - this.lumaAt >= 500) {
        this.lumaAt = t2;
        this.luma = this.measureLuma(v2);
      }
      if (this.luma !== void 0) frame.luma = this.luma;
    }
    for (const h2 of [...this.handlers]) h2(frame);
  }
  /** Mean brightness of a tiny thumbnail. The thumbnail is read and dropped here; it goes nowhere. */
  measureLuma(v2) {
    try {
      const c2 = this.lumaCanvas ??= Object.assign(document.createElement("canvas"), { width: 16, height: 9 });
      const ctx = c2.getContext("2d", { willReadFrequently: true });
      if (!ctx) return void 0;
      ctx.drawImage(v2, 0, 0, 16, 9);
      const d2 = ctx.getImageData(0, 0, 16, 9).data;
      let sum = 0;
      for (let i2 = 0; i2 < d2.length; i2 += 4) sum += 0.2126 * d2[i2] + 0.7152 * d2[i2 + 1] + 0.0722 * d2[i2 + 2];
      return sum / (d2.length / 4);
    } catch {
      return void 0;
    }
  }
  stop() {
    this.running = false;
    this.holders = 0;
    this.ticker?.terminate();
    this.ticker = null;
    this.stream?.getTracks().forEach((t2) => t2.stop());
    this.stream = null;
    this.landmarker?.close();
    this.landmarker = null;
    if (this.video) {
      this.video.srcObject = null;
      if (this.ownsVideo) this.video.remove();
    }
    this.video = null;
    this.ownsVideo = false;
    this.lastVideoTime = -1;
    this.luma = void 0;
  }
};

// src/features.ts
var LM = {
  rightOuter: 33,
  rightInner: 133,
  rightUpper: 159,
  rightLower: 145,
  rightIris: [468, 469, 470, 471, 472],
  leftInner: 362,
  leftOuter: 263,
  leftUpper: 386,
  leftLower: 374,
  leftIris: [473, 474, 475, 476, 477],
  noseTip: 1
};
var LANDMARK_COUNT = 478;
var IOD_CM = 6.3;
var CAMERA_FOV_V = 40 * Math.PI / 180;
function eye(lm, aspect, cornerA, cornerB, upper, lower, iris) {
  const p2 = (i2) => ({ x: lm[i2].x * aspect, y: lm[i2].y });
  const a2 = p2(cornerA);
  const b2 = p2(cornerB);
  const w2 = Math.hypot(b2.x - a2.x, b2.y - a2.y);
  if (!(w2 > 1e-6)) return null;
  const ax = { x: (b2.x - a2.x) / w2, y: (b2.y - a2.y) / w2 };
  const ay = { x: -ax.y, y: ax.x };
  const mid = { x: (a2.x + b2.x) / 2, y: (a2.y + b2.y) / 2 };
  let ix = 0;
  let iy = 0;
  for (const i2 of iris) {
    const q2 = p2(i2);
    ix += q2.x;
    iy += q2.y;
  }
  ix /= iris.length;
  iy /= iris.length;
  const u2 = p2(upper);
  const l2 = p2(lower);
  const along = (q2, axis) => ((q2.x - mid.x) * axis.x + (q2.y - mid.y) * axis.y) / w2;
  return {
    features: {
      x: along({ x: ix, y: iy }, ax),
      y: along({ x: ix, y: iy }, ay),
      lid: along(u2, ay),
      open: Math.hypot(u2.x - l2.x, u2.y - l2.y) / w2
    },
    mid
  };
}
function headPoint(frame, centre, iod, yaw, pitch, aspect) {
  const m2 = frame.matrix;
  if (m2 && m2.length >= 16) {
    const columnMajor = Math.abs(m2[14]) >= Math.abs(m2[11]);
    const tx2 = columnMajor ? m2[12] : m2[3];
    const ty2 = columnMajor ? m2[13] : m2[7];
    const tz = columnMajor ? m2[14] : m2[11];
    const fx = columnMajor ? m2[8] : m2[2];
    const fy = columnMajor ? m2[9] : m2[6];
    let fz = columnMajor ? m2[10] : m2[10];
    if (Number.isFinite(tx2 + ty2 + tz + fx + fy + fz) && Math.abs(tz) > 1) {
      if (Math.abs(fz) < 0.25) fz = fz < 0 ? -0.25 : 0.25;
      return {
        hx: tx2 - tz * fx / fz,
        hy: ty2 - tz * fy / fz,
        dist: Math.abs(tz),
        planeYaw: Math.atan2(fx, Math.abs(fz)),
        planePitch: Math.atan2(fy, Math.abs(fz))
      };
    }
  }
  const span = 2 * Math.tan(CAMERA_FOV_V / 2);
  const dist = IOD_CM / (iod * span);
  const tx = (centre.x - aspect / 2) * span * dist;
  const ty = (centre.y - 0.5) * span * dist;
  const tanYaw = yaw * 2.1;
  const tanPitch = (pitch - 0.556) * 2.1;
  return {
    hx: tx + dist * tanYaw,
    hy: ty + dist * tanPitch,
    dist,
    planeYaw: Math.atan(tanYaw),
    planePitch: Math.atan(tanPitch)
  };
}
function extractFeatures(frame) {
  const lm = frame.landmarks;
  if (!lm || lm.length < LANDMARK_COUNT) return null;
  const aspect = frame.aspect > 0 ? frame.aspect : 1;
  const r2 = eye(lm, aspect, LM.rightOuter, LM.rightInner, LM.rightUpper, LM.rightLower, LM.rightIris);
  const l2 = eye(lm, aspect, LM.leftInner, LM.leftOuter, LM.leftUpper, LM.leftLower, LM.leftIris);
  if (!r2 || !l2) return null;
  const dx = l2.mid.x - r2.mid.x;
  const dy = l2.mid.y - r2.mid.y;
  const iod = Math.hypot(dx, dy);
  if (!(iod > 1e-6)) return null;
  const ax = { x: dx / iod, y: dy / iod };
  const ay = { x: -ax.y, y: ax.x };
  const c2 = { x: (l2.mid.x + r2.mid.x) / 2, y: (l2.mid.y + r2.mid.y) / 2 };
  const nose = { x: lm[LM.noseTip].x * aspect, y: lm[LM.noseTip].y };
  const yaw = ((nose.x - c2.x) * ax.x + (nose.y - c2.y) * ax.y) / iod;
  const pitch = ((nose.x - c2.x) * ay.x + (nose.y - c2.y) * ay.y) / iod;
  const head = headPoint(frame, c2, iod, yaw, pitch, aspect);
  return {
    right: r2.features,
    left: l2.features,
    hx: head.hx,
    hy: head.hy,
    dist: head.dist,
    planeYaw: head.planeYaw,
    planePitch: head.planePitch,
    yaw,
    pitch,
    roll: Math.atan2(dy, dx),
    px: c2.x / aspect - 0.5,
    py: c2.y - 0.5,
    scale: iod
  };
}
var FEATURE_COUNT = 21;
var FEATURE_VERSION = 2;
var FEATURE_PENALTY = [
  0,
  // bias: this is where a camera that is off to one side is absorbed
  3e-4,
  3e-4,
  // where the head points
  1e-4,
  1e-4,
  1e-4,
  1e-4,
  // eye rotation, both eyes
  1e-3,
  1e-3,
  // eyelids
  3e-3,
  // distance
  1e-3,
  1e-3,
  1e-3,
  // eye rotation, second order
  3e-3,
  3e-3,
  // head and eye together
  3e-3,
  3e-3,
  // head, second order
  3e-3,
  // roll
  1e-3,
  1e-3,
  1e-3
  // eye rotation, third order: wide screens, where the eyes turn a long way
];
function featureVector(f2) {
  const z2 = f2.dist / 50;
  const cos = Math.cos(f2.roll);
  const sin = Math.sin(f2.roll);
  const turn = (e2) => [
    (e2.x * cos - e2.y * sin) * 8 * z2,
    (e2.x * sin + e2.y * cos) * 8 * z2
  ];
  const [rx, ry] = turn(f2.right);
  const [lx, ly] = turn(f2.left);
  const h1 = f2.hx / 20;
  const h2 = f2.hy / 20;
  const mx = (rx + lx) / 2;
  const my = (ry + ly) / 2;
  return [
    1,
    h1,
    h2,
    rx,
    ry,
    lx,
    ly,
    f2.right.lid * 8 * z2,
    f2.left.lid * 8 * z2,
    z2 - 1,
    mx * mx,
    my * my,
    mx * my,
    h1 * mx,
    h2 * my,
    h1 * h1,
    h2 * h2,
    f2.roll * 4,
    mx * mx * mx,
    my * my * my,
    mx * my * my
  ];
}
function eyeOpenness(f2) {
  return (f2.right.open + f2.left.open) / 2;
}

// src/math/ridge.ts
function ridgeFit(rows, targets, penalty) {
  const n2 = rows.length;
  const d2 = penalty.length;
  if (n2 === 0) throw new Error("ridgeFit: no samples");
  const a2 = Array.from({ length: d2 }, () => new Array(d2).fill(0));
  const b2 = new Array(d2).fill(0);
  for (let r2 = 0; r2 < n2; r2++) {
    const row = rows[r2];
    const y2 = targets[r2];
    for (let i2 = 0; i2 < d2; i2++) {
      const ri2 = row[i2];
      b2[i2] += ri2 * y2;
      const ai2 = a2[i2];
      for (let j2 = i2; j2 < d2; j2++) ai2[j2] += ri2 * row[j2];
    }
  }
  for (let i2 = 0; i2 < d2; i2++) {
    b2[i2] /= n2;
    for (let j2 = i2; j2 < d2; j2++) {
      a2[i2][j2] /= n2;
      a2[j2][i2] = a2[i2][j2];
    }
    a2[i2][i2] += penalty[i2];
  }
  return solveLinear(a2, b2);
}
function solveLinear(a2, b2) {
  const d2 = b2.length;
  for (let col = 0; col < d2; col++) {
    let pivot = col;
    for (let r2 = col + 1; r2 < d2; r2++) if (Math.abs(a2[r2][col]) > Math.abs(a2[pivot][col])) pivot = r2;
    if (Math.abs(a2[pivot][col]) < 1e-12) {
      for (let r2 = 0; r2 < d2; r2++) a2[r2][col] = 0;
      for (let j2 = 0; j2 < d2; j2++) a2[col][j2] = 0;
      a2[col][col] = 1;
      b2[col] = 0;
      continue;
    }
    if (pivot !== col) {
      [a2[pivot], a2[col]] = [a2[col], a2[pivot]];
      [b2[pivot], b2[col]] = [b2[col], b2[pivot]];
    }
    for (let r2 = col + 1; r2 < d2; r2++) {
      const f2 = a2[r2][col] / a2[col][col];
      if (f2 === 0) continue;
      for (let j2 = col; j2 < d2; j2++) a2[r2][j2] -= f2 * a2[col][j2];
      b2[r2] -= f2 * b2[col];
    }
  }
  const w2 = new Array(d2).fill(0);
  for (let i2 = d2 - 1; i2 >= 0; i2--) {
    let s2 = b2[i2];
    for (let j2 = i2 + 1; j2 < d2; j2++) s2 -= a2[i2][j2] * w2[j2];
    w2[i2] = s2 / a2[i2][i2];
  }
  return w2;
}
function dot(a2, b2) {
  let s2 = 0;
  for (let i2 = 0; i2 < a2.length; i2++) s2 += a2[i2] * b2[i2];
  return s2;
}
function median(values) {
  if (values.length === 0) return 0;
  const s2 = [...values].sort((p2, q2) => p2 - q2);
  const m2 = s2.length >> 1;
  return s2.length % 2 ? s2[m2] : (s2[m2 - 1] + s2[m2]) / 2;
}

// src/model.ts
function predict(model, phi) {
  return { x: dot(model.wx, phi), y: dot(model.wy, phi) };
}
function builtInModel() {
  const wx = new Array(FEATURE_COUNT).fill(0);
  const wy = new Array(FEATURE_COUNT).fill(0);
  wx[0] = 0.5;
  wx[3] = -0.25;
  wx[5] = -0.25;
  wy[0] = 0.5;
  wy[4] = 0.5;
  wy[6] = 0.5;
  return { version: FEATURE_VERSION, features: FEATURE_COUNT, wx, wy, createdAt: 0, crossError: 0, builtIn: true };
}
function isValidModel(m2) {
  const g2 = m2;
  return !!g2 && g2.version === FEATURE_VERSION && g2.features === FEATURE_COUNT && Array.isArray(g2.wx) && Array.isArray(g2.wy) && g2.wx.length === FEATURE_COUNT && g2.wy.length === FEATURE_COUNT && g2.wx.every(Number.isFinite) && g2.wy.every(Number.isFinite);
}

// src/learning.ts
function emptyStats() {
  return {
    n: 0,
    a: new Array(FEATURE_COUNT * FEATURE_COUNT).fill(0),
    bx: new Array(FEATURE_COUNT).fill(0),
    by: new Array(FEATURE_COUNT).fill(0)
  };
}
function addSample(stats, phi, x2, y2, weight = 1) {
  const d2 = FEATURE_COUNT;
  for (let i2 = 0; i2 < d2; i2++) {
    const wi2 = weight * phi[i2];
    stats.bx[i2] += wi2 * x2;
    stats.by[i2] += wi2 * y2;
    const row = i2 * d2;
    for (let j2 = 0; j2 < d2; j2++) stats.a[row + j2] += wi2 * phi[j2];
  }
  stats.n += weight;
}
function solveStats(stats) {
  const d2 = FEATURE_COUNT;
  const n2 = Math.max(stats.n, 1e-9);
  const system = () => {
    const m2 = [];
    for (let i2 = 0; i2 < d2; i2++) {
      const row = new Array(d2);
      for (let j2 = 0; j2 < d2; j2++) row[j2] = stats.a[i2 * d2 + j2] / n2;
      row[i2] += FEATURE_PENALTY[i2];
      m2.push(row);
    }
    return m2;
  };
  return {
    wx: solveLinear(system(), stats.bx.map((v2) => v2 / n2)),
    wy: solveLinear(system(), stats.by.map((v2) => v2 / n2))
  };
}
function isValidStats(s2) {
  const t2 = s2;
  return !!t2 && Number.isFinite(t2.n) && Array.isArray(t2.a) && t2.a.length === FEATURE_COUNT * FEATURE_COUNT && Array.isArray(t2.bx) && t2.bx.length === FEATURE_COUNT && Array.isArray(t2.by) && t2.by.length === FEATURE_COUNT && t2.a.every(Number.isFinite) && t2.bx.every(Number.isFinite) && t2.by.every(Number.isFinite);
}

// src/calibration.ts
var MIN_SAMPLES_PER_TARGET = 5;
var MIN_SAMPLES_MOVING = 3;
var MOVING_WEIGHT = 0.35;
var MIN_TARGETS = 4;
var CalibrationSession = class {
  targets = [];
  current = null;
  get collecting() {
    return this.current !== null;
  }
  /**
   * Start collecting for a point the user is looking at. Pass `headMoving` for a step where they
   * keep their eyes on the point while turning and moving their head: those samples show the fit
   * how the features change when only the head moves, which is what lets it compensate later.
   */
  beginTarget(x2, y2, headMoving = false) {
    this.endTarget();
    this.current = { x: x2, y: y2, rows: [], headMoving };
  }
  /** Add one frame's feature vector to the target being collected. Ignored between targets. */
  add(phi) {
    if (this.current && phi.length === FEATURE_COUNT && phi.every(Number.isFinite)) this.current.rows.push(phi);
  }
  /** Close the current target and return how many samples it got. */
  endTarget() {
    const t2 = this.current;
    this.current = null;
    if (!t2) return 0;
    this.targets.push(t2);
    return t2.rows.length;
  }
  fit() {
    this.endTarget();
    const usable = this.targets.map((t2) => ({ ...t2, rows: t2.headMoving ? t2.rows : trimOutliers(t2.rows) })).filter((t2) => t2.rows.length >= (t2.headMoving ? MIN_SAMPLES_MOVING : MIN_SAMPLES_PER_TARGET));
    const still = usable.filter((t2) => !t2.headMoving);
    const samples = usable.reduce((n2, t2) => n2 + t2.rows.length, 0);
    const fail = (reason) => ({
      model: null,
      result: { ok: false, reason, targets: usable.length, samples, fitError: 0, crossError: 0 }
    });
    if (still.length < MIN_TARGETS) {
      return fail(`only ${still.length} of ${this.targets.length} targets got enough good samples; keep your face in view and your eyes open`);
    }
    const stats = statsOf(usable);
    const weights = modelOf(stats);
    let sq = 0;
    let count = 0;
    for (const t2 of still) {
      for (const row of t2.rows) {
        const p2 = predict(weights, row);
        sq += (p2.x - t2.x) ** 2 + (p2.y - t2.y) ** 2;
        count++;
      }
    }
    const fitError = Math.sqrt(sq / count);
    let crossSq = 0;
    for (const held of still) {
      const p2 = predict(modelOf(statsOf(usable.filter((t2) => t2 !== held))), meanRow(held.rows));
      crossSq += (p2.x - held.x) ** 2 + (p2.y - held.y) ** 2;
    }
    const crossError = Math.sqrt(crossSq / still.length);
    if (!Number.isFinite(fitError) || !Number.isFinite(crossError)) return fail("the fit did not converge");
    const model = { ...weights, createdAt: Date.now(), crossError, stats, learned: 0 };
    return { model, result: { ok: true, targets: usable.length, samples, fitError, crossError } };
  }
};
function statsOf(targets) {
  const stats = emptyStats();
  for (const t2 of targets) {
    const weight = t2.headMoving ? MOVING_WEIGHT : 1;
    for (const row of t2.rows) addSample(stats, row, t2.x, t2.y, weight);
  }
  return stats;
}
function modelOf(stats) {
  return { version: FEATURE_VERSION, features: FEATURE_COUNT, ...solveStats(stats), createdAt: 0, crossError: 0 };
}
function meanRow(rows) {
  const m2 = new Array(FEATURE_COUNT).fill(0);
  for (const row of rows) for (let i2 = 0; i2 < FEATURE_COUNT; i2++) m2[i2] += row[i2];
  return m2.map((v2) => v2 / rows.length);
}
function trimOutliers(rows) {
  if (rows.length < 5) return rows;
  const med = [1, 2, 3, 4].map((i2) => median(rows.map((r2) => r2[i2])));
  const scored = rows.map((row) => ({
    row,
    d: Math.max(...[1, 2, 3, 4].map((i2, k2) => Math.abs(row[i2] - med[k2])))
  }));
  scored.sort((a2, b2) => a2.d - b2.d);
  return scored.slice(0, Math.ceil(rows.length * 0.8)).map((s2) => s2.row);
}
function calibrationPattern(count = 9, margin = 0.08) {
  const lo2 = margin;
  const hi2 = 1 - margin;
  const mid = 0.5;
  const q1 = lo2 + (hi2 - lo2) * 0.25;
  const q3 = lo2 + (hi2 - lo2) * 0.75;
  const five = [
    { x: mid, y: mid },
    { x: lo2, y: lo2 },
    { x: hi2, y: hi2 },
    { x: hi2, y: lo2 },
    { x: lo2, y: hi2 }
  ];
  if (count === 5) return five;
  const nine = [
    ...five,
    { x: mid, y: lo2 },
    { x: mid, y: hi2 },
    { x: lo2, y: mid },
    { x: hi2, y: mid }
  ];
  if (count === 9) return nine;
  return [
    ...nine,
    { x: q1, y: q1 },
    { x: q3, y: q3 },
    { x: q3, y: q1 },
    { x: q1, y: q3 }
  ];
}

// src/filters/fixation.ts
var DEFAULT_FIXATION = { radius: 6e-3, sigmas: 3, window: 15, recent: 4 };
var FixationFilter = class {
  constructor(options = DEFAULT_FIXATION) {
    this.options = options;
  }
  options;
  cx = 0;
  cy = 0;
  count = 0;
  rx = [];
  ry = [];
  /** Running mean of the squared distance of single estimates from the long average. */
  scatter2 = 0.02 * 0.02;
  /** True while the output is the average of a steady look. */
  fixating = false;
  reset() {
    this.count = 0;
    this.rx.length = 0;
    this.ry.length = 0;
    this.fixating = false;
  }
  /** The shift that currently counts as an eye movement. */
  get threshold() {
    const o2 = this.options;
    return Math.max(o2.radius, o2.sigmas * Math.sqrt(this.scatter2) / Math.sqrt(o2.recent));
  }
  filter(x2, y2) {
    const o2 = this.options;
    this.rx.push(x2);
    this.ry.push(y2);
    if (this.rx.length > o2.recent) {
      this.rx.shift();
      this.ry.shift();
    }
    if (this.count === 0) {
      this.cx = x2;
      this.cy = y2;
      this.count = 1;
      return { x: x2, y: y2 };
    }
    let sx = 0;
    let sy = 0;
    for (let i2 = 0; i2 < this.rx.length; i2++) {
      sx += this.rx[i2];
      sy += this.ry[i2];
    }
    sx /= this.rx.length;
    sy /= this.rx.length;
    const limit = this.threshold;
    const drift = Math.hypot(sx - this.cx, sy - this.cy);
    const latest = Math.hypot(x2 - this.cx, y2 - this.cy);
    const full = this.rx.length >= o2.recent;
    const leap = latest > limit * 4 && drift > limit;
    if (full && drift > limit || leap) {
      const from = leap ? { x: x2, y: y2 } : { x: sx, y: sy };
      this.cx = from.x;
      this.cy = from.y;
      this.count = leap ? 1 : this.rx.length;
      if (leap) {
        this.rx.length = 0;
        this.ry.length = 0;
        this.rx.push(x2);
        this.ry.push(y2);
      }
      this.fixating = false;
      return { x: this.cx, y: this.cy };
    }
    this.scatter2 += (latest * latest - this.scatter2) * 0.04;
    this.count = Math.min(this.count + 1, o2.window);
    this.cx += (x2 - this.cx) / this.count;
    this.cy += (y2 - this.cy) / this.count;
    this.fixating = this.count >= 4;
    return { x: this.cx, y: this.cy };
  }
};

// src/filters/oneEuro.ts
var DEFAULT_ONE_EURO = { minCutoff: 1.2, beta: 0.6, dCutoff: 1 };
var OneEuroFilter = class {
  constructor(options = DEFAULT_ONE_EURO) {
    this.options = options;
  }
  options;
  x = null;
  dx = 0;
  lastT = 0;
  reset() {
    this.x = null;
    this.dx = 0;
  }
  /** `tMs` is the sample time in milliseconds. */
  filter(value, tMs) {
    if (this.x === null) {
      this.x = value;
      this.lastT = tMs;
      return value;
    }
    const dt2 = Math.max((tMs - this.lastT) / 1e3, 1e-3);
    this.lastT = tMs;
    const rawDx = (value - this.x) / dt2;
    this.dx += alpha(this.options.dCutoff, dt2) * (rawDx - this.dx);
    const cutoff = this.options.minCutoff + this.options.beta * Math.abs(this.dx);
    this.x += alpha(cutoff, dt2) * (value - this.x);
    return this.x;
  }
};
function alpha(cutoffHz, dt2) {
  const tau = 1 / (2 * Math.PI * cutoffHz);
  return 1 / (1 + tau / dt2);
}

// src/pipeline.ts
var DEFAULT_PIPELINE = { filter: DEFAULT_ONE_EURO, fixation: DEFAULT_FIXATION, blinkRatio: 0.45 };
var clamp01 = (v2) => v2 < 0 ? 0 : v2 > 1 ? 1 : v2;
var GazePipeline = class {
  constructor(options = DEFAULT_PIPELINE) {
    this.options = options;
    this.fx = new OneEuroFilter(options.filter);
    this.fy = new OneEuroFilter(options.filter);
    this.fixation = options.fixation ? new FixationFilter(options.fixation) : null;
  }
  options;
  /** The fitted model, or null to use the built-in mapping. */
  model = null;
  /** The last frame's features and regression input, kept for calibration to read. */
  lastFeatures = null;
  lastVector = null;
  lastBlink = false;
  fallback = builtInModel();
  fx;
  fy;
  fixation;
  /** True while the eyes are resting on one spot. */
  fixating = false;
  openBaseline = 0;
  last = null;
  reset() {
    this.fx.reset();
    this.fy.reset();
    this.fixation?.reset();
    this.fixating = false;
    this.openBaseline = 0;
    this.last = null;
  }
  /** Pass null when the camera frame had no face in it. */
  process(frame, t2 = frame?.t ?? 0) {
    const features = frame ? extractFeatures(frame) : null;
    this.lastFeatures = features;
    this.lastVector = null;
    this.lastBlink = false;
    const calibrated = this.model !== null;
    if (!features) {
      const held = this.last;
      return {
        t: t2,
        x: held?.x ?? 0.5,
        y: held?.y ?? 0.5,
        rawX: held?.rawX ?? 0.5,
        rawY: held?.rawY ?? 0.5,
        confidence: 0,
        face: false,
        blink: false,
        calibrated
      };
    }
    const open = eyeOpenness(features);
    this.openBaseline = Math.max(open, this.openBaseline * 0.998);
    const blink = open < this.openBaseline * this.options.blinkRatio;
    this.lastBlink = blink;
    if (blink && this.last) {
      return { ...this.last, t: t2, confidence: 0, face: true, blink: true, calibrated };
    }
    const phi = featureVector(features);
    this.lastVector = phi;
    const raw = predict(this.model ?? this.fallback, phi);
    const steady = this.fixation ? this.fixation.filter(raw.x, raw.y) : raw;
    this.fixating = this.fixation?.fixating ?? false;
    const sample = {
      t: t2,
      x: clamp01(this.fx.filter(steady.x, t2)),
      y: clamp01(this.fy.filter(steady.y, t2)),
      rawX: raw.x,
      rawY: raw.y,
      confidence: confidenceOf(features),
      face: true,
      blink,
      calibrated
    };
    this.last = sample;
    return sample;
  }
};
function confidenceOf(f2) {
  const size = clamp01(f2.scale / 0.07);
  const turn = clamp01(1.4 - Math.abs(f2.yaw) * 2.5);
  return clamp01(size * turn);
}

// src/protocol.ts
var PROTOCOL_VERSION = 1;
var DEFAULT_BRIDGE_PORT = 8787;
var DEFAULT_BRIDGE_TLS_PORT = 8788;
function encode(message) {
  return JSON.stringify(message);
}
function decode(text) {
  try {
    const m2 = JSON.parse(text);
    return m2 && typeof m2 === "object" && typeof m2.type === "string" ? m2 : null;
  } catch {
    return null;
  }
}
function bridgeUrl(host = "localhost", port = DEFAULT_BRIDGE_PORT, secure = false) {
  return `${secure ? "wss" : "ws"}://${host}:${port}/gaze`;
}

// src/emitter.ts
var Emitter = class {
  handlers = /* @__PURE__ */ new Map();
  on(event, handler) {
    let set = this.handlers.get(event);
    if (!set) this.handlers.set(event, set = /* @__PURE__ */ new Set());
    set.add(handler);
    return () => set.delete(handler);
  }
  emit(event, payload) {
    const set = this.handlers.get(event);
    if (!set) return;
    for (const h2 of [...set]) h2(payload);
  }
};

// src/screenSpace.ts
function viewportOnScreen() {
  const w2 = window;
  const side = Math.max(0, (window.outerWidth - window.innerWidth) / 2);
  const chromeTop = Math.max(0, window.outerHeight - window.innerHeight - side);
  return {
    left: w2.mozInnerScreenX ?? window.screenX + side,
    top: w2.mozInnerScreenY ?? window.screenY + chromeTop,
    width: Math.max(1, window.innerWidth),
    height: Math.max(1, window.innerHeight),
    screenWidth: Math.max(1, window.screen.width),
    screenHeight: Math.max(1, window.screen.height)
  };
}
function viewportToScreen(x2, y2, v2 = viewportOnScreen()) {
  return { x: (v2.left + x2 * v2.width) / v2.screenWidth, y: (v2.top + y2 * v2.height) / v2.screenHeight };
}
function screenToViewport(x2, y2, v2 = viewportOnScreen()) {
  return { x: (x2 * v2.screenWidth - v2.left) / v2.width, y: (y2 * v2.screenHeight - v2.top) / v2.height };
}

// src/remote.ts
var BridgeLink = class extends Emitter {
  constructor(url, role, options) {
    super();
    this.url = url;
    this.role = role;
    this.options = options;
    this.connect();
  }
  url;
  role;
  options;
  socket = null;
  closed = false;
  timer = null;
  get connected() {
    return this.socket?.readyState === 1;
  }
  connect() {
    if (this.closed) return;
    const socket = new WebSocket(this.url);
    this.socket = socket;
    socket.onopen = () => {
      socket.send(
        encode({ type: "hello", v: PROTOCOL_VERSION, role: this.role, room: this.options.room, name: this.options.name })
      );
      this.emit("open", void 0);
    };
    socket.onmessage = (ev) => {
      const message = typeof ev.data === "string" ? decode(ev.data) : null;
      if (message) this.emit("message", message);
    };
    socket.onclose = () => {
      if (this.socket !== socket) return;
      this.socket = null;
      this.emit("close", void 0);
      const wait2 = this.options.reconnectMs ?? 1500;
      if (!this.closed && wait2 > 0) this.timer = setTimeout(() => this.connect(), wait2);
    };
    socket.onerror = () => socket.close();
  }
  send(message) {
    if (!this.connected) return false;
    this.socket.send(encode(message));
    return true;
  }
  close() {
    this.closed = true;
    if (this.timer) clearTimeout(this.timer);
    this.socket?.close();
  }
};
var FAILED = { ok: false, targets: 0, samples: 0, fitError: 0, crossError: 0 };
var RemoteGazeSource = class extends Emitter {
  latest = null;
  trackers = 0;
  link;
  nextId = 1;
  waiting = /* @__PURE__ */ new Map();
  space;
  constructor(url, options = {}) {
    super();
    this.space = options.space ?? (typeof window !== "undefined" && typeof window.screen !== "undefined" ? "viewport" : "display");
    this.link = new BridgeLink(url, "display", options);
    this.link.on("open", () => this.emit("connection", true));
    this.link.on("close", () => {
      this.trackers = 0;
      for (const done of this.waiting.values()) done(null);
      this.waiting.clear();
      this.emit("connection", false);
    });
    this.link.on("message", (m2) => {
      if (m2.type === "gaze") {
        const sample = this.toViewport(m2);
        this.latest = sample;
        this.emit("gaze", sample);
      } else if (m2.type === "peers" || m2.type === "welcome") {
        this.trackers = m2.trackers;
        this.emit("peers", { type: "peers", trackers: m2.trackers, displays: m2.displays });
      } else if (m2.type === "calib.collected" || m2.type === "calib.result") {
        const done = this.waiting.get(m2.id);
        this.waiting.delete(m2.id);
        done?.(m2);
      }
    });
  }
  get connected() {
    return this.link.connected;
  }
  toViewport(sample) {
    if (this.space !== "viewport") return sample;
    const v2 = viewportOnScreen();
    const clamp2 = (n2) => n2 < 0 ? 0 : n2 > 1 ? 1 : n2;
    const smooth = screenToViewport(sample.x, sample.y, v2);
    const raw = screenToViewport(sample.rawX, sample.rawY, v2);
    return { ...sample, x: clamp2(smooth.x), y: clamp2(smooth.y), rawX: raw.x, rawY: raw.y };
  }
  request(id, timeoutMs) {
    return new Promise((resolve) => {
      const timer = setTimeout(() => {
        this.waiting.delete(id);
        resolve(null);
      }, timeoutMs);
      this.waiting.set(id, (m2) => {
        clearTimeout(timer);
        resolve(m2);
      });
    });
  }
  calibBegin() {
    this.link.send({ type: "calib", op: "begin" });
  }
  async calibCollect(x2, y2, ms2, headMoving = false) {
    const id = this.nextId++;
    const target = this.space === "viewport" ? viewportToScreen(x2, y2) : { x: x2, y: y2 };
    if (!this.link.send({ type: "calib", op: "collect", id, x: target.x, y: target.y, ms: ms2, headMoving })) return 0;
    const reply = await this.request(id, ms2 + 3e3);
    return reply?.type === "calib.collected" ? reply.samples : 0;
  }
  async calibFinish() {
    const id = this.nextId++;
    if (!this.link.send({ type: "calib", op: "finish", id })) return { ...FAILED, reason: "not connected to the bridge" };
    const reply = await this.request(id, 8e3);
    if (reply?.type !== "calib.result") return { ...FAILED, reason: "the tracker did not answer" };
    const { type: _type, id: _id, ...result } = reply;
    return result;
  }
  calibClear() {
    this.link.send({ type: "calib", op: "clear" });
  }
  hint(x2, y2, weight = 0.5) {
    const target = this.space === "viewport" ? viewportToScreen(x2, y2) : { x: x2, y: y2 };
    this.link.send({ type: "calib", op: "hint", x: target.x, y: target.y, weight });
  }
  close() {
    this.link.close();
  }
};
function publishToBridge(source, url, options = {}) {
  const link = new BridgeLink(url, "tracker", options);
  let displays = 0;
  let changed = () => {
  };
  const off = source.on("gaze", (g2) => {
    if (displays > 0) link.send({ type: "gaze", ...g2 });
  });
  link.on("open", () => changed());
  link.on("close", () => {
    displays = 0;
    changed();
  });
  link.on("message", async (m2) => {
    if (m2.type === "peers" || m2.type === "welcome") {
      displays = m2.displays;
      changed();
    } else if (m2.type === "calib") {
      if (m2.op === "begin") source.calibBegin();
      else if (m2.op === "clear") source.calibClear();
      else if (m2.op === "hint") source.hint?.(m2.x, m2.y, m2.weight);
      else if (m2.op === "collect") {
        const samples = await source.calibCollect(m2.x, m2.y, m2.ms, m2.headMoving === true);
        link.send({ type: "calib.collected", id: m2.id, samples });
      } else if (m2.op === "finish") {
        const result = await source.calibFinish();
        link.send({ type: "calib.result", id: m2.id, ...result });
      }
    }
  });
  return {
    get connected() {
      return link.connected;
    },
    get displays() {
      return displays;
    },
    onChange(handler) {
      changed = handler;
    },
    close() {
      off();
      link.close();
    }
  };
}

// src/testing/syntheticFace.ts
function seededRandom(seed) {
  let a2 = seed >>> 0;
  return () => {
    a2 = a2 + 1831565813 >>> 0;
    let t2 = a2;
    t2 = Math.imul(t2 ^ t2 >>> 15, t2 | 1);
    t2 ^= t2 + Math.imul(t2 ^ t2 >>> 7, t2 | 61);
    return ((t2 ^ t2 >>> 14) >>> 0) / 4294967296;
  };
}
var EYE_HALF_GAP = 3.15;
var EYE_HALF_WIDTH = 1.4;
var EYEBALL_RADIUS = 1.2;
function syntheticFace(o2) {
  const aspect = o2.aspect ?? 16 / 9;
  const random = o2.random ?? Math.random;
  const screenW = o2.screenWidthCm ?? 60;
  const screenH = o2.screenHeightCm ?? 34;
  const cameraX = o2.cameraX ?? 0.5;
  const cameraY = o2.cameraY ?? -0.03;
  const tilt = o2.cameraTilt ?? 0;
  const yaw = o2.yaw ?? 0;
  const pitch = o2.pitch ?? 0;
  const roll = o2.roll ?? 0;
  const head = [o2.headXCm ?? 0, o2.headYCm ?? 12, o2.distanceCm ?? 55];
  const ct2 = Math.cos(tilt);
  const st2 = Math.sin(tilt);
  const sx = -(o2.gazeX - cameraX) * screenW;
  const sy = (o2.gazeY - cameraY) * screenH;
  const target = [sx, sy * ct2, -sy * st2];
  const cy = Math.cos(yaw);
  const syw = Math.sin(yaw);
  const cp = Math.cos(pitch);
  const sp = Math.sin(pitch);
  const cr2 = Math.cos(roll);
  const sr2 = Math.sin(roll);
  const rot = (v2) => {
    const x1 = v2[0] * cr2 - v2[1] * sr2;
    const y1 = v2[0] * sr2 + v2[1] * cr2;
    const z1 = v2[2];
    const y2 = y1 * cp - z1 * sp;
    const z2 = y1 * sp + z1 * cp;
    const x3 = x1 * cy + z2 * syw;
    const z3 = -x1 * syw + z2 * cy;
    return [x3, y2, z3];
  };
  const toCamera = (v2) => {
    const w2 = rot(v2);
    return [head[0] + w2[0], head[1] + w2[1], head[2] - w2[2]];
  };
  const unrot = (d2) => {
    const x1 = d2[0] * cy - d2[2] * syw;
    const z1 = d2[0] * syw + d2[2] * cy;
    const y2 = d2[1] * cp + z1 * sp;
    const z2 = -d2[1] * sp + z1 * cp;
    return [x1 * cr2 + y2 * sr2, -x1 * sr2 + y2 * cr2, z2];
  };
  const span = 2 * Math.tan(CAMERA_FOV_V / 2);
  const jitter = () => o2.noise ? (random() - 0.5) * 2 * o2.noise * (2 * EYE_HALF_WIDTH) : 0;
  const project = (v2) => {
    const c2 = toCamera([v2[0] + jitter(), v2[1] + jitter(), v2[2]]);
    return { x: 0.5 + c2[0] / c2[2] / (span * aspect), y: 0.5 + c2[1] / c2[2] / span, z: 0 };
  };
  const centre = project([0, 0, 0]);
  const landmarks = Array.from({ length: LANDMARK_COUNT }, () => ({ ...centre }));
  const placeEye = (side, cornerA, cornerB, upper, lower, iris) => {
    const eyeCentre = [side * EYE_HALF_GAP, 0, 0];
    const world = toCamera(eyeCentre);
    const d2 = unrot([target[0] - world[0], target[1] - world[1], -(target[2] - world[2])]);
    const len = Math.hypot(d2[0], d2[1], d2[2]);
    const ix = d2[0] / len * EYEBALL_RADIUS;
    const iy = d2[1] / len * EYEBALL_RADIUS;
    const at2 = (dx, dy) => project([eyeCentre[0] + dx, eyeCentre[1] + dy, 0]);
    landmarks[cornerA] = at2(-EYE_HALF_WIDTH, 0);
    landmarks[cornerB] = at2(EYE_HALF_WIDTH, 0);
    landmarks[upper] = at2(ix * 0.3, (o2.blink ? 0.03 : -0.5) + iy * 0.6);
    landmarks[lower] = at2(ix * 0.3, (o2.blink ? 0.06 : 0.42) + iy * 0.2);
    const r2 = 0.35;
    landmarks[iris[0]] = at2(ix, iy);
    landmarks[iris[1]] = at2(ix + r2, iy);
    landmarks[iris[2]] = at2(ix, iy - r2);
    landmarks[iris[3]] = at2(ix - r2, iy);
    landmarks[iris[4]] = at2(ix, iy + r2);
  };
  placeEye(-1, LM.rightOuter, LM.rightInner, LM.rightUpper, LM.rightLower, LM.rightIris);
  placeEye(1, LM.leftInner, LM.leftOuter, LM.leftUpper, LM.leftLower, LM.leftIris);
  landmarks[LM.noseTip] = project([0, 3.5, 3]);
  const frame = { landmarks, aspect, t: o2.t ?? 0 };
  if (o2.matrix) {
    const ax = rot([1, 0, 0]);
    const ay = rot([0, 1, 0]);
    const az = rot([0, 0, 1]);
    frame.matrix = [
      ax[0],
      ax[1],
      -ax[2],
      0,
      ay[0],
      ay[1],
      -ay[2],
      0,
      az[0],
      az[1],
      -az[2],
      0,
      head[0],
      head[1],
      head[2],
      1
    ];
  }
  return frame;
}

// src/events.ts
function epochClock() {
  const offset = typeof performance !== "undefined" ? Date.now() - performance.now() : 0;
  return (t2) => Math.round(offset + t2);
}
function spoken(ms2) {
  const s2 = Math.max(0, Math.round(ms2 / 1e3));
  return s2 < 60 ? `${s2} s` : `${Math.floor(s2 / 60)} min ${s2 % 60} s`;
}
var Episode = class {
  constructor(minOnMs, minOffMs) {
    this.minOnMs = minOnMs;
    this.minOffMs = minOffMs;
  }
  minOnMs;
  minOffMs;
  active = false;
  /** When the active episode began (the first moment the condition held). */
  start = 0;
  pendingSince = null;
  lastTrue = 0;
  /** Feed the condition at time `t`. Returns what changed. */
  update(t2, holds) {
    if (!this.active) {
      if (!holds) {
        this.pendingSince = null;
        return {};
      }
      this.pendingSince ??= t2;
      this.lastTrue = t2;
      if (t2 - this.pendingSince >= this.minOnMs) {
        this.active = true;
        this.start = this.pendingSince;
        this.pendingSince = null;
        return { started: true };
      }
      return {};
    }
    if (holds) {
      this.lastTrue = t2;
      return {};
    }
    if (t2 - this.lastTrue >= this.minOffMs) {
      this.active = false;
      return { ended: { start: this.start, end: this.lastTrue } };
    }
    return {};
  }
  /** How long the active episode has run, up to `t`. */
  duration(t2) {
    return this.active ? Math.max(0, t2 - this.start) : 0;
  }
  /** Close an active episode now (the camera stopped). */
  close(t2) {
    if (!this.active) {
      this.pendingSince = null;
      return null;
    }
    this.active = false;
    return { start: this.start, end: Math.max(this.start, Math.min(t2, this.lastTrue + this.minOffMs)) };
  }
};

// src/presence.ts
var DEFAULT_PRESENCE = {
  absentMs: 2e3,
  multipleMs: 1e3,
  driftMs: 3e3,
  qualityMs: 3e3,
  enrollFrames: 45,
  driftThreshold: 0.08,
  minLuma: 45,
  minScale: 0.045
};
var PAIRS = [
  [33, 133],
  // right eye width
  [362, 263],
  // left eye width
  [159, 145],
  // right eye height
  [386, 374],
  // left eye height
  [129, 358],
  // nose width
  [168, 1],
  // nose length
  [61, 291],
  // mouth width
  [234, 454],
  // face width
  [10, 152],
  // face height
  [172, 397],
  // jaw width
  [105, 159],
  // right brow to eye
  [334, 386],
  // left brow to eye
  [1, 13],
  // nose to mouth
  [13, 152],
  // mouth to chin
  [116, 345],
  // cheekbone width
  [10, 168]
  // forehead to nose bridge
];
function faceSignature(frame) {
  const lm = frame.landmarks;
  if (!lm || lm.length < 468) return null;
  const a2 = frame.aspect > 0 ? frame.aspect : 1;
  const d2 = (i2, j2) => Math.hypot((lm[i2].x - lm[j2].x) * a2, lm[i2].y - lm[j2].y);
  const mid = (i2, j2) => ({ x: (lm[i2].x + lm[j2].x) / 2 * a2, y: (lm[i2].y + lm[j2].y) / 2 });
  const r2 = mid(33, 133);
  const l2 = mid(362, 263);
  const iod = Math.hypot(l2.x - r2.x, l2.y - r2.y);
  if (!(iod > 1e-6)) return null;
  return PAIRS.map(([i2, j2]) => d2(i2, j2) / iod);
}
function isFrontal(frame) {
  const f2 = extractFeatures(frame);
  if (!f2) return false;
  return Math.abs(f2.yaw) < 0.22 && Math.abs(f2.pitch - 0.556) < 0.25 && Math.abs(f2.roll) < 0.35;
}
function signatureDistance(a2, b2) {
  let sum = 0;
  let n2 = 0;
  for (let i2 = 0; i2 < a2.length; i2++) {
    const m2 = Math.max(a2[i2], b2[i2]);
    if (!(m2 > 1e-3)) continue;
    sum += Math.abs(a2[i2] - b2[i2]) / m2;
    n2++;
  }
  return n2 === 0 ? 0 : sum / n2;
}
function median2(values) {
  const s2 = [...values].sort((x2, y2) => x2 - y2);
  const m2 = s2.length >> 1;
  return s2.length % 2 ? s2[m2] : (s2[m2 - 1] + s2[m2]) / 2;
}
var PresenceMonitor = class extends Emitter {
  options;
  status = { faces: 0, absent: false, multiple: false, identity: "unenrolled", warnings: [] };
  epoch;
  absent;
  multiple;
  drift;
  quality;
  /** In-memory only. No getter, no serialisation. */
  #reference = null;
  #samples = [];
  #recent = null;
  #worst = 0;
  #lastT = 0;
  unsubscribe = null;
  constructor(options = {}) {
    super();
    this.options = { ...DEFAULT_PRESENCE, ...options };
    const o2 = this.options;
    this.epoch = o2.epoch ?? epochClock();
    this.absent = new Episode(o2.absentMs, 400);
    this.multiple = new Episode(o2.multipleMs, 600);
    this.drift = new Episode(o2.driftMs, 1500);
    const q2 = () => new Episode(o2.qualityMs, 1e3);
    this.quality = { "low-light": q2(), "face-too-small": q2(), "face-cut-off": new Episode(2e3, 1e3), "face-off-center": q2() };
  }
  /** Listen to a WebcamCore (or any frame source). Returns the function that stops listening. */
  connect(source) {
    this.unsubscribe?.();
    this.unsubscribe = source.subscribe((f2) => this.process(f2));
    this.emit("status", this.status);
    return () => {
      this.unsubscribe?.();
      this.unsubscribe = null;
    };
  }
  /** Forget the reference face and start enrolling again from the next frontal frames. */
  reset() {
    this.#reference = null;
    this.#samples = [];
    this.#recent = null;
    this.#worst = 0;
  }
  get enrolled() {
    return this.#reference !== null;
  }
  /** Close open episodes (the camera stopped, the exam ended) so each is reported with an end. */
  flush(t2 = this.#lastT) {
    this.closeEpisode(this.absent, "face-absent", t2, "integrity");
    this.closeEpisode(this.multiple, "multiple-faces", t2, "integrity");
    this.closeEpisode(this.drift, "identity-drift", t2, "integrity");
    for (const key of Object.keys(this.quality)) {
      this.closeEpisode(this.quality[key], key, t2, "quality");
    }
  }
  process(frame) {
    const t2 = frame.t;
    this.#lastT = t2;
    const o2 = this.options;
    const n2 = frame.faces.length;
    const primary = frame.faces[0];
    const features = primary ? extractFeatures(primary) : null;
    this.step(this.absent, "face-absent", t2, n2 === 0, "integrity");
    this.step(this.multiple, "multiple-faces", t2, n2 >= 2, "integrity");
    let driftNow = false;
    if (n2 === 1 && primary && isFrontal(primary)) {
      const sig = faceSignature(primary);
      if (sig) {
        if (!this.#reference) {
          this.#samples.push(sig);
          if (this.#samples.length >= o2.enrollFrames) {
            this.#reference = sig.map((_2, k2) => median2(this.#samples.map((s3) => s3[k2])));
            this.#samples = [];
          }
        } else {
          const d2 = signatureDistance(this.#reference, sig);
          this.#recent = this.#recent === null ? d2 : this.#recent * 0.9 + d2 * 0.1;
          driftNow = this.#recent > o2.driftThreshold;
          if (driftNow) this.#worst = Math.max(this.#worst, this.#recent);
        }
      }
    }
    const identityKnown = n2 === 1 && primary && isFrontal(primary);
    if (this.#reference && identityKnown) this.step(this.drift, "identity-drift", t2, driftNow, "integrity");
    const lowLight = frame.luma !== void 0 && frame.luma < o2.minLuma;
    this.step(this.quality["low-light"], "low-light", t2, lowLight, "quality");
    this.step(this.quality["face-too-small"], "face-too-small", t2, !!features && features.scale < o2.minScale, "quality");
    const cutOff = !!primary && [10, 152, 234, 454].some((i2) => {
      const p2 = primary.landmarks[i2];
      return p2.x < 0.01 || p2.x > 0.99 || p2.y < 0.01 || p2.y > 0.99;
    });
    this.step(this.quality["face-cut-off"], "face-cut-off", t2, cutOff, "quality");
    this.step(
      this.quality["face-off-center"],
      "face-off-center",
      t2,
      !!features && (Math.abs(features.px) > 0.3 || Math.abs(features.py) > 0.3),
      "quality"
    );
    const warnings = Object.keys(this.quality).filter((k2) => this.quality[k2].active);
    const identity = !this.#reference ? this.#samples.length > 0 ? "enrolling" : "unenrolled" : this.drift.active ? "drift" : "match";
    const next = { faces: n2, absent: this.absent.active, multiple: this.multiple.active, identity, warnings };
    const s2 = this.status;
    const changed = s2.faces !== next.faces || s2.absent !== next.absent || s2.multiple !== next.multiple || s2.identity !== next.identity || s2.warnings.join() !== next.warnings.join();
    this.status = next;
    if (changed) this.emit("status", next);
  }
  step(ep, type, t2, holds, category) {
    const r2 = ep.update(t2, holds);
    if (r2.started) this.emit("begin", { type, start: this.epoch(ep.start), category });
    if (r2.ended) this.finish(type, r2.ended.start, r2.ended.end, category);
  }
  closeEpisode(ep, type, t2, category) {
    const r2 = ep.close(t2);
    if (r2) this.finish(type, r2.start, r2.end, category);
  }
  finish(type, start, end, category) {
    const span = spoken(end - start);
    const worst = this.#worst;
    if (type === "identity-drift") this.#worst = 0;
    const [confidence, summary] = (() => {
      switch (type) {
        case "face-absent":
          return [0.9, `No face was in view for ${span}.`];
        case "multiple-faces":
          return [0.7, `More than one face was in view for ${span}. It may be a person, a photo or a reflection.`];
        case "identity-drift":
          return [
            Math.min(0.6, 0.3 + worst * 2),
            `The face in view looked different from the one at the start for ${span}. Face geometry is a weak signal; it may be a different person or a change in pose, lighting or glasses.`
          ];
        case "low-light":
          return [0.9, `The picture was too dark for reliable face tracking for ${span}.`];
        case "face-too-small":
          return [0.9, `The face was too small in the picture for ${span}; sitting closer helps.`];
        case "face-cut-off":
          return [0.9, `Part of the face was outside the picture for ${span}.`];
        case "face-off-center":
          return [0.9, `The face was far from the middle of the picture for ${span}.`];
      }
    })();
    this.emit("event", { type, start: this.epoch(start), end: this.epoch(end), confidence, summary, category });
  }
};

// src/headpose.ts
var DEG = 180 / Math.PI;
var clamp = (v2, lo2, hi2) => v2 < lo2 ? lo2 : v2 > hi2 ? hi2 : v2;
var EYE_K_X = 0.4;
var EYE_K_Y = 0.32;
function estimateGazeAngles(frame) {
  const f2 = extractFeatures(frame);
  if (!f2) return null;
  if (f2.right.open + f2.left.open < 0.14) return null;
  const headYaw = -f2.planeYaw * DEG;
  const headPitch = -f2.planePitch * DEG;
  const ix = (f2.right.x + f2.left.x) / 2;
  const iy = (f2.right.y + f2.left.y) / 2;
  const eyeYaw = -Math.asin(clamp(ix / EYE_K_X, -0.9, 0.9)) * DEG;
  const eyePitch = -Math.asin(clamp(iy / EYE_K_Y, -0.9, 0.9)) * DEG;
  const size = clamp(f2.scale / 0.07, 0, 1);
  const turn = clamp(1.4 - Math.abs(f2.yaw) * 2.5, 0, 1);
  return {
    yawDeg: headYaw + eyeYaw,
    pitchDeg: headPitch + eyePitch,
    headYawDeg: headYaw,
    headPitchDeg: headPitch,
    eyeYawDeg: eyeYaw,
    eyePitchDeg: eyePitch,
    confidence: size * turn
  };
}
function regionLimitsFor(screenWidthCm, screenHeightCm, distanceCm, slackDeg = 4) {
  const half = (cm) => Math.atan(cm / 2 / Math.max(10, distanceCm)) * DEG + slackDeg;
  return { halfWidthDeg: half(screenWidthCm), halfHeightDeg: half(screenHeightCm) };
}
var DEFAULT_HEADPOSE = {
  halfWidthDeg: 18,
  halfHeightDeg: 11,
  baselineFrames: 45,
  switchMs: 150,
  thinkingMs: 4e3,
  sustainedMs: 6e3,
  minConfidence: 0.25
};
var WORDS = {
  left: "to the left",
  right: "to the right",
  up: "up",
  down: "down"
};
var HeadPoseMonitor = class extends Emitter {
  options;
  latest = null;
  epoch;
  baseYaw = 0;
  basePitch = 0;
  baseN = 0;
  ready = false;
  smYaw = 0;
  smPitch = 0;
  smReady = false;
  region = "on-screen";
  regionSince = 0;
  candidate = null;
  candidateSince = 0;
  peak = 0;
  sustainedReported = false;
  lastSeen = 0;
  unsubscribe = null;
  constructor(options = {}) {
    super();
    this.options = { ...DEFAULT_HEADPOSE, ...options };
    this.epoch = this.options.epoch ?? epochClock();
  }
  connect(source) {
    this.unsubscribe?.();
    this.unsubscribe = source.subscribe((f2) => this.process(f2));
    return () => {
      this.unsubscribe?.();
      this.unsubscribe = null;
    };
  }
  /** Throw the baseline away and learn it again from the next frames (the person moved their seat or screen). */
  recalibrate() {
    this.ready = false;
    this.baseN = 0;
    this.baseYaw = 0;
    this.basePitch = 0;
    this.smReady = false;
  }
  get baselineReady() {
    return this.ready;
  }
  /** The face has been gone (or the camera stopped): close the current stay without judging it. */
  flush(t2 = this.lastSeen) {
    this.finishSegment(t2);
    this.region = "on-screen";
    this.regionSince = t2;
    this.candidate = null;
    this.sustainedReported = false;
  }
  process(frame) {
    const t2 = frame.t;
    const face = frame.faces[0];
    const g2 = face ? estimateGazeAngles(face) : null;
    if (!face) {
      if (this.latest && t2 - this.lastSeen > 1e3) this.flush(this.lastSeen);
      return;
    }
    if (!g2 || g2.confidence < this.options.minConfidence) return;
    this.lastSeen = t2;
    const o2 = this.options;
    if (!this.smReady) {
      this.smYaw = g2.yawDeg;
      this.smPitch = g2.pitchDeg;
      this.smReady = true;
    } else {
      this.smYaw += (g2.yawDeg - this.smYaw) * 0.4;
      this.smPitch += (g2.pitchDeg - this.smPitch) * 0.4;
    }
    if (!this.ready) {
      this.baseN++;
      this.baseYaw += (this.smYaw - this.baseYaw) / this.baseN;
      this.basePitch += (this.smPitch - this.basePitch) / this.baseN;
      if (this.baseN >= o2.baselineFrames) this.ready = true;
      this.regionSince ||= t2;
      this.publish(t2, "on-screen", 0, 0, g2.confidence);
      return;
    }
    const dyaw = this.smYaw - this.baseYaw;
    const dpitch = this.smPitch - this.basePitch;
    const nx = dyaw / o2.halfWidthDeg;
    const ny = dpitch / o2.halfHeightDeg;
    const edge = this.region === "on-screen" ? 1 : 0.75;
    let want;
    if (Math.max(Math.abs(nx), Math.abs(ny)) <= edge) want = "on-screen";
    else if (Math.abs(nx) >= Math.abs(ny)) want = nx > 0 ? "right" : "left";
    else want = ny > 0 ? "up" : "down";
    if (want === "on-screen") {
      if (this.region === "on-screen" && Math.max(Math.abs(nx), Math.abs(ny)) < 0.6) {
        this.baseYaw += dyaw * 4e-3;
        this.basePitch += dpitch * 4e-3;
      }
    }
    if (want !== this.region) {
      if (this.candidate !== want) {
        this.candidate = want;
        this.candidateSince = t2;
      }
      if (t2 - this.candidateSince >= o2.switchMs) {
        this.finishSegment(this.candidateSince);
        this.region = want;
        this.regionSince = this.candidateSince;
        this.candidate = null;
        this.peak = 0;
        this.sustainedReported = false;
      }
    } else {
      this.candidate = null;
    }
    if (this.region !== "on-screen") this.peak = Math.max(this.peak, Math.hypot(dyaw, dpitch));
    const dwell = t2 - this.regionSince;
    if (this.region !== "on-screen" && !this.sustainedReported && dwell >= o2.sustainedMs) {
      this.sustainedReported = true;
      this.emit("begin", { type: "gaze-off-screen", start: this.epoch(this.regionSince), category: "integrity" });
    }
    this.publish(t2, this.region, dyaw, dpitch, g2.confidence);
  }
  publish(t2, region, yawDeg, pitchDeg, confidence) {
    const s2 = {
      t: t2,
      region,
      yawDeg,
      pitchDeg,
      dwellMs: Math.max(0, t2 - this.regionSince),
      confidence,
      ready: this.ready
    };
    this.latest = s2;
    this.emit("sample", s2);
  }
  finishSegment(end) {
    if (!this.ready || end <= this.regionSince) return;
    const o2 = this.options;
    const dwellMs = end - this.regionSince;
    const off = this.region !== "on-screen";
    const thinking = off && dwellMs < o2.thinkingMs;
    const seg = { region: this.region, start: this.regionSince, end, dwellMs, thinking, peakDeg: this.peak };
    this.emit("segment", seg);
    if (!off) return;
    const region = this.region;
    if (thinking) {
      this.emit("event", {
        type: "thinking-glance",
        category: "annotation",
        region,
        dwellMs,
        start: this.epoch(seg.start),
        end: this.epoch(seg.end),
        confidence: 0.6,
        summary: `Looked ${WORDS[region]} for ${spoken(dwellMs)}, a short glance of the kind people make while thinking. Not a concern on its own.`
      });
    } else if (dwellMs >= o2.sustainedMs) {
      this.emit("event", {
        type: "gaze-off-screen",
        category: "integrity",
        region,
        dwellMs,
        start: this.epoch(seg.start),
        end: this.epoch(seg.end),
        confidence: clamp(0.3 + (dwellMs - o2.sustainedMs) / 6e4, 0.3, 0.55),
        summary: `Looked ${WORDS[region]} for ${spoken(dwellMs)}. Long looks away can be thinking, reading something, or a distraction; read it in context with the answer given.`
      });
    }
  }
};

// src/reading.ts
var DEFAULT_READING = {
  windowMs: 3e4,
  minCycles: 3,
  minSweepDeg: 4,
  maxSweepSpeedDegPerSec: 28,
  minSweepMs: 500,
  returnSpeedRatio: 2.5,
  endAfterMs: 5e3,
  gapMs: 1500
};
function zigzag(points, thr) {
  const pivots = [];
  if (points.length === 0) return pivots;
  let trend = 0;
  let lo2 = points[0];
  let hi2 = points[0];
  let cand = points[0];
  for (let i2 = 1; i2 < points.length; i2++) {
    const p2 = points[i2];
    if (trend === 0) {
      if (p2.v < lo2.v) lo2 = p2;
      if (p2.v > hi2.v) hi2 = p2;
      if (p2.v - lo2.v > thr && lo2.t <= p2.t) {
        pivots.push(lo2);
        trend = 1;
        cand = p2;
      } else if (hi2.v - p2.v > thr) {
        pivots.push(hi2);
        trend = -1;
        cand = p2;
      }
    } else if (trend > 0) {
      if (p2.v > cand.v) cand = p2;
      else if (cand.v - p2.v > thr) {
        pivots.push(cand);
        trend = -1;
        cand = p2;
      }
    } else {
      if (p2.v < cand.v) cand = p2;
      else if (p2.v - cand.v > thr) {
        pivots.push(cand);
        trend = 1;
        cand = p2;
      }
    }
  }
  if (trend !== 0) pivots.push(cand);
  return pivots;
}
var ReadingPatternDetector = class extends Emitter {
  options;
  epoch;
  buf = [];
  region = null;
  lastOff = 0;
  lastCheck = 0;
  active = null;
  constructor(options = {}) {
    super();
    this.options = { ...DEFAULT_READING, ...options };
    this.epoch = this.options.epoch ?? epochClock();
  }
  push(s2) {
    const o2 = this.options;
    if (!s2.ready) return;
    if (s2.region === "on-screen") {
      if (this.region !== null && s2.t - this.lastOff > o2.gapMs) this.reset(s2.t);
      return;
    }
    if (this.region !== null && this.region !== s2.region) this.reset(s2.t);
    this.region = s2.region;
    this.lastOff = s2.t;
    this.buf.push({ t: s2.t, v: s2.yawDeg });
    while (this.buf.length && s2.t - this.buf[0].t > o2.windowMs) this.buf.shift();
    if (s2.t - this.lastCheck >= 200) {
      this.lastCheck = s2.t;
      this.check(s2.t);
    }
  }
  /** Close an open report (the exam ended). */
  flush(t2) {
    this.finish(t2);
    this.buf = [];
    this.region = null;
  }
  reset(t2) {
    this.finish(t2);
    this.buf = [];
    this.region = null;
  }
  finish(_t2) {
    const a2 = this.active;
    if (!a2) return;
    this.active = null;
    const span = a2.end - a2.start;
    const confidence = Math.min(0.85, 0.35 + 0.1 * a2.cycles) * (a2.sweep >= 6 ? 1 : 0.85);
    this.emit("event", {
      type: "reading-pattern",
      category: "integrity",
      region: a2.region,
      cycles: a2.cycles,
      start: this.epoch(a2.start),
      end: this.epoch(a2.end),
      confidence: Math.round(confidence * 100) / 100,
      summary: `The eyes swept ${a2.sign > 0 ? "toward the right" : "toward the left"} and jumped back ${a2.cycles} times in ${spoken(span)} while looking ${a2.region === "up" || a2.region === "down" ? a2.region : "to the " + a2.region} of the screen, the movement of reading text placed there. It can have other explanations; check it against the answers given.`
    });
  }
  check(t2) {
    const o2 = this.options;
    const cycles = this.findCycles();
    if (cycles.length >= o2.minCycles) {
      const plus = cycles.filter((c2) => c2.sign > 0).length;
      const sign = plus >= cycles.length - plus ? 1 : -1;
      const same = cycles.filter((c2) => c2.sign === sign);
      if (same.length >= o2.minCycles) {
        const start = same[0].start;
        const end = same[same.length - 1].end;
        const sweep = same.reduce((s2, c2) => s2 + c2.sweepDeg, 0) / same.length;
        if (!this.active) {
          this.active = { start, end, cycles: same.length, sign, region: this.region ?? "off-screen", sweep };
          this.emit("begin", { type: "reading-pattern", start: this.epoch(start), category: "integrity" });
        } else {
          this.active.end = Math.max(this.active.end, end);
          this.active.cycles = Math.max(this.active.cycles, same.length);
          this.active.sweep = sweep;
        }
      }
    }
    if (this.active && t2 - this.active.end > o2.endAfterMs) this.finish(t2);
  }
  findCycles() {
    const o2 = this.options;
    const piv = zigzag(this.buf, o2.minSweepDeg * 0.5);
    const cycles = [];
    for (let i2 = 0; i2 + 2 < piv.length; i2++) {
      const a2 = piv[i2];
      const b2 = piv[i2 + 1];
      const c2 = piv[i2 + 2];
      const sweep = b2.v - a2.v;
      const back = c2.v - b2.v;
      const dur = b2.t - a2.t;
      const backDur = Math.max(1, c2.t - b2.t);
      if (Math.abs(sweep) < o2.minSweepDeg || Math.sign(sweep) === Math.sign(back)) continue;
      if (dur < o2.minSweepMs) continue;
      const speed = Math.abs(sweep) / dur * 1e3;
      const backSpeed = Math.abs(back) / backDur * 1e3;
      if (speed > o2.maxSweepSpeedDegPerSec) continue;
      if (backSpeed < speed * o2.returnSpeedRatio) continue;
      if (Math.abs(back) < Math.abs(sweep) * 0.5) continue;
      cycles.push({ start: a2.t, end: c2.t, sign: Math.sign(sweep), sweepDeg: Math.abs(sweep) });
      i2++;
    }
    return cycles;
  }
};

// src/integrity.ts
var PRIVACY = Object.freeze({
  mode: "no-frames-leave-device",
  guarantee: "No video frame, image, landmark or face signature is encoded, stored or sent by this library. Modules receive landmark numbers in memory; the only pixel read is a 16x9 brightness thumbnail that is averaged to one number and dropped. The only output is a list of events (type, start, end, confidence, summary), each passed through sanitizeEvent. The same-person signature lives in memory inside the presence monitor and has no export method.",
  leaves: ["type", "start", "end", "confidence", "summary"]
});
var MAX_TYPE = 60;
var MAX_SUMMARY = 500;
function sanitizeEvent(e2) {
  if (!e2 || typeof e2.type !== "string" || !Number.isFinite(e2.start)) return null;
  const clip = (s2, max) => typeof s2 === "string" ? s2.replace(/data:[^\s]*/gi, "").slice(0, max) : "";
  const confidence = Number.isFinite(e2.confidence) ? Math.min(1, Math.max(0, e2.confidence)) : 0;
  return {
    type: clip(e2.type, MAX_TYPE),
    start: Math.round(e2.start),
    end: e2.end === null || !Number.isFinite(e2.end) ? null : Math.round(e2.end),
    confidence: Math.round(confidence * 100) / 100,
    summary: clip(e2.summary, MAX_SUMMARY)
  };
}
function toHubEvent(e2) {
  const s2 = sanitizeEvent(e2);
  if (!s2) return null;
  const confidence = e2.category === "annotation" ? Math.min(s2.confidence, 0.3) : s2.confidence;
  return {
    source: "webcam",
    type: s2.type,
    startedAt: new Date(s2.start).toISOString(),
    endedAt: s2.end === null ? null : new Date(s2.end).toISOString(),
    confidence,
    summary: s2.summary
  };
}
var DEFAULT_INTEGRITY = { includeQuality: false, includeAnnotations: true };
var IntegrityMonitor = class extends Emitter {
  options;
  presence;
  headpose;
  reading;
  /** Always the same value: nothing but sanitized events leaves. */
  privacy = PRIVACY;
  offs = [];
  held = null;
  constructor(options = {}) {
    super();
    this.options = { ...DEFAULT_INTEGRITY, ...options };
    const o2 = this.options;
    this.presence = o2.presence === false ? null : new PresenceMonitor(o2.presence);
    this.headpose = o2.headpose === false ? null : new HeadPoseMonitor(o2.headpose);
    this.reading = o2.reading === false || !this.headpose ? null : new ReadingPatternDetector(o2.reading);
    const forward = (module) => (e2) => {
      if (e2.category === "quality" && !this.options.includeQuality) return;
      if (e2.category === "annotation" && !this.options.includeAnnotations) return;
      const s2 = sanitizeEvent(e2);
      if (s2) this.emit("event", { ...s2, module, category: e2.category });
    };
    const begins = (module) => (b2) => {
      if (b2.category === "integrity") this.emit("begin", { type: b2.type, start: b2.start, module });
    };
    this.presence?.on("event", forward("presence"));
    this.presence?.on("begin", begins("presence"));
    this.headpose?.on("event", forward("headpose"));
    this.headpose?.on("begin", begins("headpose"));
    this.reading?.on("event", forward("reading"));
    this.reading?.on("begin", begins("reading"));
    if (this.reading) this.headpose?.on("sample", (s2) => this.reading?.push(s2));
  }
  /** Listen to frames from any source (a WebcamCore, or synthetic ones in a demo). Does not touch the camera. */
  connect(source) {
    const offs = [this.presence?.connect(source), this.headpose?.connect(source)].filter((f2) => !!f2);
    const off = () => {
      offs.forEach((f2) => f2());
      this.presence?.flush();
      this.headpose?.flush();
      this.reading?.flush();
    };
    this.offs.push(off);
    return off;
  }
  /** Start the camera on `core` (if nobody has) and listen. The camera stops again on `stop()`. */
  async start(core) {
    if (this.held) return;
    await core.retain();
    this.held = core;
    this.connect(core);
  }
  /** Stop listening, close open events so each has an end, and release the camera. */
  stop() {
    this.offs.forEach((f2) => f2());
    this.offs = [];
    this.held?.release();
    this.held = null;
  }
  /**
   * Batch events for a server: calls `send` with up to `maxBatch` hub events at a time (the server
   * accepts 50 per call), every `everyMs` and on `flush()`. Failed sends are put back and retried.
   */
  sink(send, options = {}) {
    const maxBatch = Math.min(50, options.maxBatch ?? 50);
    const queue = [];
    let busy = false;
    const flush = async () => {
      if (busy) return;
      busy = true;
      try {
        while (queue.length) {
          const batch = queue.splice(0, maxBatch);
          try {
            await send(batch);
          } catch {
            queue.unshift(...batch);
            break;
          }
        }
      } finally {
        busy = false;
      }
    };
    const off = this.on("event", (e2) => {
      const h2 = toHubEvent(e2);
      if (h2) queue.push(h2);
    });
    const timer = setInterval(() => void flush(), options.everyMs ?? 5e3);
    return {
      flush,
      close: async () => {
        off();
        clearInterval(timer);
        await flush();
      }
    };
  }
};

// src/camera.ts
var CameraLandmarkSource = class {
  options;
  core;
  ownsCore;
  unsubscribe = null;
  held = false;
  constructor(options = {}, core) {
    this.options = { ...DEFAULT_CAMERA, ...options };
    this.ownsCore = !core;
    this.core = core ?? new WebcamCore(this.options);
  }
  get video() {
    return this.core.video;
  }
  async start(onFrame) {
    if (this.held) return;
    this.unsubscribe = this.core.subscribe((f2) => onFrame(f2.faces[0] ?? null, f2.t));
    try {
      await this.core.retain();
      this.held = true;
    } catch (err) {
      this.unsubscribe();
      this.unsubscribe = null;
      throw err;
    }
  }
  stop() {
    this.unsubscribe?.();
    this.unsubscribe = null;
    if (this.held) {
      this.held = false;
      if (this.ownsCore) this.core.stop();
      else this.core.release();
    }
  }
  /** The cameras the browser knows about. Labels are empty until camera permission is granted. */
  static listCameras() {
    return WebcamCore.listCameras();
  }
};

// src/eyeTracker.ts
var STORAGE_PREFIX = "mindattic.eyetracker.model.v3.";
var EyeTracker = class extends Emitter {
  camera;
  pipeline;
  latest = null;
  status = { state: "idle", fps: 0, face: false, calibrated: false };
  session = null;
  storageKey;
  space;
  pendingHint = null;
  unsolved = 0;
  onClick = (ev) => {
    if (this.session || this.status.state !== "running") return;
    this.hint(ev.clientX / Math.max(1, window.innerWidth), ev.clientY / Math.max(1, window.innerHeight));
  };
  frames = 0;
  fpsSince = 0;
  constructor(options = {}) {
    super();
    this.camera = new CameraLandmarkSource(options.camera, options.core);
    this.pipeline = new GazePipeline({ ...DEFAULT_PIPELINE, ...options.pipeline });
    this.storageKey = options.storageKey === void 0 ? "default" : options.storageKey;
    this.space = options.space ?? "viewport";
    this.loadCalibration();
    if ((options.learnFromClicks ?? this.space === "viewport") && typeof window !== "undefined") {
      window.addEventListener("pointerdown", this.onClick, true);
    }
  }
  get video() {
    return this.camera.video;
  }
  get calibrated() {
    return this.pipeline.model !== null;
  }
  /** Ask for the camera, load the face model and start emitting gaze. Rejects if either fails. */
  async start() {
    if (this.status.state === "running" || this.status.state === "starting") return;
    this.setStatus({ state: "starting", error: void 0 });
    try {
      this.fpsSince = performance.now();
      this.frames = 0;
      await this.camera.start((frame, t2) => this.onFrame(frame, t2));
      this.setStatus({ state: "running" });
    } catch (err) {
      this.camera.stop();
      this.setStatus({ state: "error", error: describeCameraError(err) });
      throw err;
    }
  }
  stop() {
    this.camera.stop();
    this.pipeline.reset();
    this.setStatus({ state: "idle", fps: 0, face: false });
  }
  onFrame(frame, t2) {
    const sample = this.toOutputSpace(this.pipeline.process(frame, t2));
    if (this.session?.collecting && this.pipeline.lastVector && !this.pipeline.lastBlink) {
      this.session.add(this.pipeline.lastVector);
    }
    this.learn();
    this.latest = sample;
    this.frames++;
    if (t2 - this.fpsSince >= 1e3) {
      this.setStatus({ fps: Math.round(this.frames * 1e3 / (t2 - this.fpsSince)), face: sample.face });
      this.frames = 0;
      this.fpsSince = t2;
    } else if (sample.face !== this.status.face) {
      this.setStatus({ face: sample.face });
    }
    this.emit("frame", { frame, features: this.pipeline.lastFeatures });
    this.emit("gaze", sample);
  }
  /** The pipeline works in screen space; hand out the point relative to the page as it is right now. */
  toOutputSpace(sample) {
    if (this.space !== "viewport") return sample;
    const v2 = viewportOnScreen();
    const clamp2 = (n2) => n2 < 0 ? 0 : n2 > 1 ? 1 : n2;
    const smooth = screenToViewport(sample.x, sample.y, v2);
    const raw = screenToViewport(sample.rawX, sample.rawY, v2);
    return { ...sample, x: clamp2(smooth.x), y: clamp2(smooth.y), rawX: raw.x, rawY: raw.y };
  }
  setStatus(patch) {
    this.status = { ...this.status, ...patch, calibrated: this.calibrated };
    this.emit("status", this.status);
  }
  // ---- learning in use ----
  /**
   * The user is looking at (x, y) now. The next few frames are added to the fit as extra
   * calibration samples, at a fraction of the weight of a real calibration sample.
   */
  hint(x2, y2, weight = 0.5) {
    if (!this.pipeline.model?.stats || this.session) return;
    const target = this.space === "viewport" ? viewportToScreen(x2, y2) : { x: x2, y: y2 };
    this.pendingHint = { x: target.x, y: target.y, weight, frames: 4 };
  }
  /** How many samples have been learned since the calibration. */
  get learned() {
    return this.pipeline.model?.learned ?? 0;
  }
  learn() {
    const hint = this.pendingHint;
    const model = this.pipeline.model;
    const phi = this.pipeline.lastVector;
    if (!hint || !model?.stats) return;
    if (!phi || this.pipeline.lastBlink) return;
    hint.frames--;
    if (hint.frames <= 0) this.pendingHint = null;
    const now = predict(model, phi);
    if (Math.hypot(now.x - hint.x, now.y - hint.y) > 0.3) return;
    addSample(model.stats, phi, hint.x, hint.y, hint.weight);
    model.learned = (model.learned ?? 0) + 1;
    this.unsolved++;
    if (this.unsolved >= 4) {
      this.unsolved = 0;
      const solved = solveStats(model.stats);
      if (solved.wx.every(Number.isFinite) && solved.wy.every(Number.isFinite)) {
        model.wx = solved.wx;
        model.wy = solved.wy;
        this.saveCalibration();
      }
    }
  }
  // ---- calibration (GazeSource) ----
  calibBegin() {
    this.session = new CalibrationSession();
  }
  calibCollect(x2, y2, ms2, headMoving = false) {
    const session = this.session ??= new CalibrationSession();
    const target = this.space === "viewport" ? viewportToScreen(x2, y2) : { x: x2, y: y2 };
    session.beginTarget(target.x, target.y, headMoving);
    return new Promise((resolve) => setTimeout(() => resolve(session.endTarget()), ms2));
  }
  async calibFinish() {
    const session = this.session;
    this.session = null;
    if (!session) return { ok: false, reason: "no calibration in progress", targets: 0, samples: 0, fitError: 0, crossError: 0 };
    const { model, result } = session.fit();
    if (model) this.setModel(model);
    return result;
  }
  calibClear() {
    this.session = null;
    this.setModel(null);
  }
  /** Install a model (for example one restored from your own storage), or null for the built-in mapping. */
  setModel(model) {
    this.pipeline.model = model;
    this.pipeline.reset();
    this.saveCalibration();
    this.setStatus({});
  }
  saveCalibration() {
    if (!this.storageKey) return;
    try {
      const key = STORAGE_PREFIX + this.storageKey;
      if (this.pipeline.model) localStorage.setItem(key, JSON.stringify(this.pipeline.model));
      else localStorage.removeItem(key);
    } catch {
    }
  }
  loadCalibration() {
    if (!this.storageKey) return;
    try {
      const text = localStorage.getItem(STORAGE_PREFIX + this.storageKey);
      const model = text ? JSON.parse(text) : null;
      if (isValidModel(model)) {
        if (model.stats && !isValidStats(model.stats)) delete model.stats;
        this.pipeline.model = model;
      }
    } catch {
    }
  }
};
function describeCameraError(err) {
  const name = err?.name;
  if (name === "NotAllowedError") return "Camera permission was refused. Allow the camera for this page and try again.";
  if (name === "NotFoundError" || name === "OverconstrainedError") return "No camera was found.";
  if (name === "NotReadableError") return "The camera is in use by another app.";
  return err instanceof Error ? err.message : String(err);
}

// src/calibrationUI.ts
var DEFAULT_CALIBRATION_UI = {
  points: 9,
  settleMs: 700,
  collectMs: 900,
  fullscreen: false,
  headMovement: true,
  pursuit: true,
  pursuitMs: 24e3,
  intro: "Look at each dot until it fills. Keep your head still and move only your eyes."
};
var POOR_CALIBRATION = 0.2;
var wait = (ms2) => new Promise((resolve) => setTimeout(resolve, ms2));
async function runCalibration(source, options = {}) {
  const o2 = { ...DEFAULT_CALIBRATION_UI, ...options };
  const targets = o2.targets ?? calibrationPattern(o2.points);
  const host = o2.container ?? document.body;
  const overlay = document.createElement("div");
  overlay.setAttribute("role", "dialog");
  overlay.setAttribute("aria-label", "Eye tracking calibration");
  overlay.style.cssText = `position:${o2.container ? "absolute" : "fixed"};inset:0;z-index:2147483000;background:#05070d;color:#e8eefc;font:500 18px/1.5 system-ui,sans-serif;cursor:none;overflow:hidden;touch-action:none`;
  const message = document.createElement("div");
  message.style.cssText = "position:absolute;left:50%;top:38%;transform:translate(-50%,-50%);max-width:34em;text-align:center;padding:0 24px";
  const dot2 = document.createElement("div");
  dot2.style.cssText = "position:absolute;width:44px;height:44px;margin:-22px 0 0 -22px;border-radius:50%;border:3px solid #7fd4ff;box-sizing:border-box;display:none;transition:left .32s cubic-bezier(.3,.7,.2,1),top .32s cubic-bezier(.3,.7,.2,1)";
  const fill = document.createElement("div");
  fill.style.cssText = "position:absolute;left:50%;top:50%;width:8px;height:8px;margin:-4px 0 0 -4px;border-radius:50%;background:#fff";
  dot2.appendChild(fill);
  overlay.append(message, dot2);
  host.appendChild(overlay);
  let cancelled = false;
  const onKey = (ev) => {
    if (ev.key === "Escape") cancelled = true;
  };
  window.addEventListener("keydown", onKey);
  const cleanup = async () => {
    window.removeEventListener("keydown", onKey);
    overlay.remove();
    if (o2.fullscreen && document.fullscreenElement) await document.exitFullscreen().catch(() => {
    });
  };
  const cancelResult = {
    ok: false,
    reason: "cancelled",
    targets: 0,
    samples: 0,
    fitError: 0,
    crossError: 0
  };
  try {
    message.innerHTML = "";
    const lead = document.createElement("p");
    lead.textContent = o2.intro;
    const hint = document.createElement("p");
    hint.style.cssText = "opacity:.6;font-size:15px";
    hint.textContent = "Click or press Space to begin. Esc cancels.";
    message.append(lead, hint);
    overlay.style.cursor = "pointer";
    await new Promise((resolve) => {
      const go2 = (ev) => {
        if (ev instanceof KeyboardEvent && ev.key !== " " && ev.key !== "Enter" && ev.key !== "Escape") return;
        overlay.removeEventListener("pointerdown", go2);
        window.removeEventListener("keydown", go2);
        resolve();
      };
      overlay.addEventListener("pointerdown", go2);
      window.addEventListener("keydown", go2);
    });
    if (cancelled) return cancelResult;
    overlay.style.cursor = "none";
    if (o2.fullscreen && !document.fullscreenElement) {
      await Promise.race([document.documentElement.requestFullscreen().catch(() => {
      }), wait(1500)]);
      await wait(300);
    }
    message.style.display = "none";
    dot2.style.display = "block";
    source.calibBegin();
    for (const target of targets) {
      if (cancelled) return cancelResult;
      dot2.style.left = `${target.x * 100}%`;
      dot2.style.top = `${target.y * 100}%`;
      fill.style.transition = "none";
      fill.style.transform = "scale(1)";
      await wait(o2.settleMs);
      if (cancelled) return cancelResult;
      fill.style.transition = `transform ${o2.collectMs}ms linear`;
      fill.style.transform = "scale(4.6)";
      await source.calibCollect(target.x, target.y, o2.collectMs);
    }
    if (cancelled) return cancelResult;
    if (o2.pursuit) {
      const prompt = document.createElement("div");
      prompt.style.cssText = "position:absolute;left:50%;bottom:9%;transform:translateX(-50%);max-width:36em;text-align:center;padding:0 24px";
      prompt.textContent = "Now follow the dot with your eyes as it moves. Keep your head still.";
      overlay.appendChild(prompt);
      const path = (ms2) => ({
        x: 0.5 + 0.43 * Math.sin(2 * Math.PI * ms2 / 8e3),
        y: 0.5 + 0.41 * Math.sin(2 * Math.PI * ms2 / 6e3 + Math.PI / 2)
      });
      const place = (p2) => {
        dot2.style.left = `${p2.x * 100}%`;
        dot2.style.top = `${p2.y * 100}%`;
      };
      fill.style.transition = "none";
      fill.style.transform = "scale(2.4)";
      place(path(0));
      await wait(1300);
      dot2.style.transition = "none";
      const began = performance.now();
      let running = true;
      const animate = () => {
        if (!running) return;
        place(path(performance.now() - began));
        requestAnimationFrame(animate);
      };
      requestAnimationFrame(animate);
      const slice = 200;
      while (performance.now() - began < o2.pursuitMs && !cancelled) {
        const middle = path(performance.now() - began + slice / 2);
        await source.calibCollect(middle.x, middle.y, slice, true);
      }
      running = false;
      dot2.style.transition = "left .32s cubic-bezier(.3,.7,.2,1),top .32s cubic-bezier(.3,.7,.2,1)";
      prompt.remove();
      if (cancelled) return cancelResult;
    }
    if (o2.headMovement) {
      const prompt = document.createElement("div");
      prompt.style.cssText = "position:absolute;left:50%;bottom:9%;transform:translateX(-50%);max-width:36em;text-align:center;padding:0 24px";
      overlay.appendChild(prompt);
      dot2.style.borderColor = "#ffc266";
      const steps = [
        { x: 0.5, y: 0.5, ms: 6e3, text: "Keep your eyes on the dot and slowly turn your head left and right, then nod up and down." },
        { x: 0.18, y: 0.22, ms: 4e3, text: "Eyes on the dot again. Lean a little closer, then back, then side to side." },
        { x: 0.82, y: 0.78, ms: 4e3, text: "Once more: eyes on the dot, move your head around gently." }
      ];
      for (const step of steps) {
        if (cancelled) return cancelResult;
        prompt.textContent = step.text;
        dot2.style.left = `${step.x * 100}%`;
        dot2.style.top = `${step.y * 100}%`;
        fill.style.transition = "none";
        fill.style.transform = "scale(1)";
        await wait(1200);
        if (cancelled) return cancelResult;
        fill.style.transition = `transform ${step.ms}ms linear`;
        fill.style.transform = "scale(4.6)";
        await source.calibCollect(step.x, step.y, step.ms, true);
      }
      prompt.remove();
      if (cancelled) return cancelResult;
    }
    dot2.style.display = "none";
    message.style.display = "block";
    message.textContent = "Working it out...";
    const result = await source.calibFinish();
    const percent = Math.round(result.crossError * 100);
    const poor = result.ok && result.crossError > POOR_CALIBRATION;
    message.textContent = !result.ok ? `Calibration failed: ${result.reason ?? "unknown reason"}.` : poor ? `Calibrated, but poorly: about ${percent}% of the screen off. Face the light, keep your head still and try again.` : `Calibrated. Typical error: about ${percent}% of the screen.`;
    await wait(result.ok && !poor ? 1100 : 2600);
    return result;
  } finally {
    await cleanup();
  }
}
function showGazeDot(source, container = document.body) {
  const dot2 = document.createElement("div");
  dot2.style.cssText = "position:fixed;left:0;top:0;width:36px;height:36px;margin:-18px 0 0 -18px;border-radius:50%;z-index:2147482000;pointer-events:none;border:2px solid rgba(127,212,255,.9);background:rgba(127,212,255,.18);will-change:transform";
  container.appendChild(dot2);
  const off = source.on("gaze", (g2) => {
    dot2.style.transform = `translate(${g2.x * window.innerWidth}px,${g2.y * window.innerHeight}px)`;
    dot2.style.opacity = g2.confidence > 0 ? "1" : ".3";
  });
  return () => {
    off();
    dot2.remove();
  };
}

// src/mouseSource.ts
var MouseGazeSource = class extends Emitter {
  /** `target` is the element whose box is the display space. Defaults to the viewport. */
  constructor(target = window) {
    super();
    this.target = target;
  }
  target;
  latest = null;
  x = 0.5;
  y = 0.5;
  raf = 0;
  onMove = (ev) => {
    const r2 = this.target === window ? null : this.target.getBoundingClientRect();
    const w2 = r2 ? r2.width : window.innerWidth;
    const h2 = r2 ? r2.height : window.innerHeight;
    this.x = Math.min(1, Math.max(0, (ev.clientX - (r2?.left ?? 0)) / w2));
    this.y = Math.min(1, Math.max(0, (ev.clientY - (r2?.top ?? 0)) / h2));
  };
  start() {
    if (this.raf) return;
    window.addEventListener("pointermove", this.onMove);
    window.addEventListener("pointerdown", this.onMove);
    const tick = () => {
      const sample = {
        t: performance.now(),
        x: this.x,
        y: this.y,
        rawX: this.x,
        rawY: this.y,
        confidence: 1,
        face: true,
        blink: false,
        calibrated: true
      };
      this.latest = sample;
      this.emit("gaze", sample);
      this.raf = requestAnimationFrame(tick);
    };
    this.raf = requestAnimationFrame(tick);
  }
  stop() {
    cancelAnimationFrame(this.raf);
    this.raf = 0;
    window.removeEventListener("pointermove", this.onMove);
    window.removeEventListener("pointerdown", this.onMove);
  }
  calibBegin() {
  }
  calibCollect(_x, _y, ms2, _headMoving) {
    return new Promise((resolve) => setTimeout(() => resolve(1), ms2));
  }
  async calibFinish() {
    return { ok: true, targets: 0, samples: 0, fitError: 0, crossError: 0 };
  }
  calibClear() {
  }
};
export {
  CAMERA_FOV_V,
  CalibrationSession,
  CameraLandmarkSource,
  DEFAULT_BRIDGE_PORT,
  DEFAULT_BRIDGE_TLS_PORT,
  DEFAULT_CALIBRATION_UI,
  DEFAULT_CAMERA,
  DEFAULT_FIXATION,
  DEFAULT_HEADPOSE,
  DEFAULT_INTEGRITY,
  DEFAULT_MODEL_URL,
  DEFAULT_ONE_EURO,
  DEFAULT_PIPELINE,
  DEFAULT_PRESENCE,
  DEFAULT_READING,
  DEFAULT_WASM_URL,
  Emitter,
  Episode,
  EyeTracker,
  FEATURE_COUNT,
  FEATURE_PENALTY,
  FEATURE_VERSION,
  FixationFilter,
  GazePipeline,
  HeadPoseMonitor,
  IOD_CM,
  IntegrityMonitor,
  LANDMARK_COUNT,
  LM,
  MEDIAPIPE_VERSION,
  MIN_SAMPLES_MOVING,
  MIN_SAMPLES_PER_TARGET,
  MIN_TARGETS,
  MOVING_WEIGHT,
  MouseGazeSource,
  OneEuroFilter,
  POOR_CALIBRATION,
  PRIVACY,
  PROTOCOL_VERSION,
  PresenceMonitor,
  ReadingPatternDetector,
  RemoteGazeSource,
  WebcamCore,
  addSample,
  bridgeUrl,
  builtInModel,
  calibrationPattern,
  decode,
  dot,
  emptyStats,
  encode,
  epochClock,
  estimateGazeAngles,
  extractFeatures,
  eyeOpenness,
  faceSignature,
  featureVector,
  isFrontal,
  isValidModel,
  isValidStats,
  median,
  predict,
  publishToBridge,
  regionLimitsFor,
  ridgeFit,
  runCalibration,
  sanitizeEvent,
  screenToViewport,
  seededRandom,
  showGazeDot,
  signatureDistance,
  solveLinear,
  solveStats,
  spoken,
  syntheticFace,
  toHubEvent,
  viewportOnScreen,
  viewportToScreen,
  zigzag
};
//# sourceMappingURL=webcam.browser.js.map
